import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:student_sync/core/api/api_exception.dart';
import 'package:student_sync/core/auth/auth_api.dart';
import 'package:student_sync/core/auth/auth_controller.dart';
import 'package:student_sync/core/auth/token_store.dart';

import '../support/fake_backend.dart';

void main() {
  group('AuthController token refresh', () {
    test('uses the stored access token while it is still valid', () async {
      final h = await TestHarness.signedIn();
      expect(await h.auth.freshAccessToken(), 'access-0');
      expect(h.backend.refreshCalls, 0);
    });

    test('concurrent callers with an expired token share ONE refresh (rotation-safe)', () async {
      final h = await TestHarness.signedIn(accessExpired: true);
      final tokens = await Future.wait(List.generate(5, (_) => h.auth.freshAccessToken()));
      expect(h.backend.refreshCalls, 1, reason: 'a rotated refresh token must never be presented twice');
      expect(tokens.toSet(), {'access-1'});
      expect(h.backend.familyRevoked, isFalse);
      expect(h.store.value!.refreshToken, 'refresh-1', reason: 'the rotated refresh token is persisted');
    });

    test('sequential refreshes present the newest refresh token each time', () async {
      final h = await TestHarness.signedIn(accessExpired: true);
      await h.auth.freshAccessToken();
      h.clock.value = h.clock.value.add(const Duration(minutes: 16));
      await h.auth.freshAccessToken();
      expect(h.backend.refreshCalls, 2);
      expect(h.backend.familyRevoked, isFalse);
      expect(h.auth.isSignedIn, isTrue);
    });

    test('a rejected refresh signs the user out with reason sessionExpired', () async {
      final h = await TestHarness.signedIn(accessExpired: true);
      h.backend.familyRevoked = true;
      expect(await h.auth.freshAccessToken(), isNull);
      expect(h.auth.status, AuthStatus.signedOut);
      expect(h.auth.lastSignOutReason, SignOutReason.sessionExpired);
      expect(h.store.value, isNull, reason: 'tokens are wiped from secure storage');
    });

    test('a network failure during refresh keeps the session and surfaces a retryable error', () async {
      final h = await TestHarness.signedIn(accessExpired: true);
      h.backend.refreshOffline = true;
      await expectLater(h.auth.freshAccessToken(), throwsA(isA<ApiException>().having((e) => e.isNetwork, 'isNetwork', true)));
      expect(h.auth.isSignedIn, isTrue);
      expect(h.store.value, isNotNull);
      // Once back online the next call refreshes normally.
      h.backend.refreshOffline = false;
      expect(await h.auth.freshAccessToken(), 'access-1');
    });

    test('refreshAfterUnauthorized returns the already-renewed token without another refresh', () async {
      final h = await TestHarness.signedIn(accessExpired: true);
      await h.auth.freshAccessToken(); // access-0 -> access-1
      final token = await h.auth.refreshAfterUnauthorized('access-0');
      expect(token, 'access-1');
      expect(h.backend.refreshCalls, 1);
    });

    test('an expired refresh token at restore starts signed out', () async {
      final store = InMemoryTokenStore();
      final h = await TestHarness.signedIn();
      store.value = StoredSession(
        accessToken: 'a',
        accessTokenExpiresAtUtc: DateTime.utc(2020),
        refreshToken: 'r',
        refreshTokenExpiresAtUtc: DateTime.utc(2020),
        user: h.auth.user!,
      );
      final auth = AuthController(api: AuthApi(baseUrl: baseUrl, httpClient: h.backend.client), store: store, clientName: 't', clock: h.clock.call);
      await auth.restore();
      expect(auth.status, AuthStatus.signedOut);
      expect(store.value, isNull);
    });
  });

  group('ApiClient', () {
    test('retries once with a renewed token after a 401', () async {
      final h = await TestHarness.signedIn();
      // The server has already rotated (e.g. token revoked server-side): first call 401s.
      h.backend.currentAccess = 'server-side-changed';
      h.backend.onApi = (req, bearer) => http.Response(jsonEncode({'ok': true}), 200);
      // Make the refresh issue a token the server accepts.
      final result = await h.api.send('GET', '/me');
      expect(result, {'ok': true});
      expect(h.backend.refreshCalls, 1);
      expect(h.backend.log.where((l) => l == 'GET /me').length, 2);
    });

    test('parallel 401s trigger exactly one refresh', () async {
      final h = await TestHarness.signedIn();
      h.backend.currentAccess = 'rotated-elsewhere';
      h.backend.onApi = (req, bearer) => http.Response('[]', 200);
      final results = await Future.wait([for (var i = 0; i < 4; i++) h.api.send('GET', '/sessions')]);
      expect(results, everyElement(isEmpty));
      expect(h.backend.refreshCalls, 1);
      expect(h.backend.familyRevoked, isFalse);
    });

    test('when renewal fails the call throws session_expired and the user is signed out', () async {
      final h = await TestHarness.signedIn();
      h.backend.currentAccess = 'nope';
      h.backend.familyRevoked = true;
      await expectLater(h.api.send('GET', '/me'), throwsA(isA<ApiException>().having((e) => e.code, 'code', 'session_expired')));
      expect(h.auth.status, AuthStatus.signedOut);
    });

    test('password_change_required flags the session so the router can redirect', () async {
      final h = await TestHarness.signedIn();
      h.backend.onApi = (req, bearer) => FakeBackend.problem(403, 'password_change_required', 'Change your temporary password to continue.');
      await expectLater(h.api.send('GET', '/dashboard/student'), throwsA(isA<ApiException>()));
      expect(h.auth.mustChangePassword, isTrue);
    });

    test('maps RFC 7807 validation problems to field errors', () async {
      final h = await TestHarness.signedIn();
      h.backend.onApi = (req, bearer) => FakeBackend.problem(400, 'validation_failed', 'Please correct the errors.', {
            'errors': {
              'title': ['Title is required.'],
            },
          });
      try {
        await h.api.post('/doubts', body: {});
        fail('expected ApiException');
      } on ApiException catch (e) {
        expect(e.isValidation, isTrue);
        expect(e.fieldError('Title'), 'Title is required.');
        expect(e.message, 'Please correct the errors.');
      }
    });

    test('sends JSON bodies, query parameters and Idempotency-Key headers', () async {
      final h = await TestHarness.signedIn();
      late http.Request seen;
      h.backend.onApi = (req, bearer) {
        seen = req;
        return http.Response('', 204);
      };
      await h.api.put('/assignments/x/my-submission', body: {'submit': true}, headers: {'Idempotency-Key': 'k1'});
      expect(seen.headers['Idempotency-Key'], 'k1');
      expect(seen.headers['Content-Type'], contains('application/json'));
      expect(jsonDecode(seen.body), {'submit': true});

      final uri = h.api.buildUri('/sessions', {'fromUtc': DateTime.utc(2026, 11, 1, 6), 'limit': 50, 'toUtc': null});
      expect(uri.queryParameters, {'fromUtc': '2026-11-01T06:00:00.000Z', 'limit': '50'});
    });
  });

  group('Sign-in', () {
    test('institution_code_required is surfaced as a typed error', () async {
      final h = await TestHarness.signedIn();
      await h.auth.signOut();
      await expectLater(
        h.auth.signIn(email: 'shared@example.test', password: 'Correct-Password1'),
        throwsA(isA<ApiException>().having((e) => e.code, 'code', 'institution_code_required')),
      );
      await h.auth.signIn(email: 'shared@example.test', password: 'Correct-Password1', institutionCode: 'HORIZON');
      expect(h.auth.isSignedIn, isTrue);
      expect(h.store.value!.institutionCode, 'HORIZON');
    });

    test('wrong password leaves the user signed out', () async {
      final h = await TestHarness.signedIn();
      await h.auth.signOut();
      await expectLater(h.auth.signIn(email: 'a@example.test', password: 'bad'), throwsA(isA<ApiException>()));
      expect(h.auth.status, AuthStatus.signedOut);
    });
  });
}
