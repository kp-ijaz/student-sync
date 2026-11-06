import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:student_sync/core/api/api_client.dart';
import 'package:student_sync/core/auth/auth_api.dart';
import 'package:student_sync/core/auth/auth_controller.dart';
import 'package:student_sync/core/auth/token_store.dart';
import 'package:student_sync/core/models/auth_models.dart';

import 'fixtures.dart';

const baseUrl = 'https://api.example.test/api/v1';

/// In-memory stand-in for the auth endpoints that behaves like the real server:
/// refresh tokens rotate, and presenting a used refresh token revokes the family.
class FakeBackend {
  FakeBackend({required this.now});

  DateTime Function() now;
  int refreshCalls = 0;
  int loginCalls = 0;
  int _counter = 0;
  String currentAccess = 'access-0';
  String currentRefresh = 'refresh-0';
  final Set<String> usedRefreshTokens = {};
  bool familyRevoked = false;

  /// Simulates latency on refresh so concurrent callers overlap.
  Duration refreshDelay = const Duration(milliseconds: 20);

  /// When set, refresh fails with a transport error.
  bool refreshOffline = false;

  /// Handler for non-auth API paths. Receives the request and the bearer token.
  FutureOr<http.Response> Function(http.Request request, String? bearer)? onApi;

  final List<String> log = [];

  http.Client get client => MockClient(_handle);

  Future<http.Response> _handle(http.Request request) async {
    final path = request.url.path.replaceFirst('/api/v1', '');
    log.add('${request.method} $path');
    switch (path) {
      case '/auth/login':
        loginCalls++;
        final body = jsonDecode(request.body) as Map<String, dynamic>;
        if (body['institutionCode'] == null && body['email'] == 'shared@example.test') {
          return _problem(400, 'institution_code_required', 'This email is used at more than one institution. Enter your institution code.');
        }
        if (body['password'] != 'Correct-Password1') return _problem(401, 'invalid_credentials', 'The email or password is incorrect.');
        return _issue();
      case '/auth/refresh':
        refreshCalls++;
        await Future<void>.delayed(refreshDelay);
        if (refreshOffline) throw http.ClientException('offline');
        final token = (jsonDecode(request.body) as Map<String, dynamic>)['refreshToken'] as String;
        if (familyRevoked || usedRefreshTokens.contains(token) || token != currentRefresh) {
          familyRevoked = true; // reuse detection revokes everything
          return _problem(401, 'invalid_refresh_token', 'Your session has expired. Please sign in again.');
        }
        usedRefreshTokens.add(token);
        return _issue();
      case '/auth/logout':
        return http.Response('', 204);
    }
    final auth = request.headers['Authorization'];
    final bearer = auth?.startsWith('Bearer ') ?? false ? auth!.substring(7) : null;
    if (bearer != currentAccess) return http.Response('', 401);
    final handler = onApi;
    if (handler == null) return http.Response('{}', 200);
    return handler(request, bearer);
  }

  http.Response _issue() {
    _counter++;
    currentAccess = 'access-$_counter';
    currentRefresh = 'refresh-$_counter';
    return http.Response(
      jsonEncode(authJson(access: currentAccess, refresh: currentRefresh, accessExpires: now().add(const Duration(minutes: 15)))),
      200,
      headers: {'content-type': 'application/json'},
    );
  }

  static http.Response _problem(int status, String code, String title, [Map<String, Object?>? extra]) => http.Response(
        jsonEncode({'type': 'https://example.test/errors/$code', 'title': title, 'status': status, 'code': code, ...?extra}),
        status,
        headers: {'content-type': 'application/problem+json'},
      );

  static http.Response problem(int status, String code, String title, [Map<String, Object?>? extra]) => _problem(status, code, title, extra);
}

class TestHarness {
  TestHarness._(this.backend, this.store, this.auth, this.api, this.clock);

  final FakeBackend backend;
  final InMemoryTokenStore store;
  final AuthController auth;
  final ApiClient api;
  final MutableClock clock;

  static Future<TestHarness> signedIn({bool accessExpired = false}) async {
    final clock = MutableClock(DateTime.utc(2026, 11, 1, 10));
    final backend = FakeBackend(now: clock.call);
    final store = InMemoryTokenStore()
      ..value = StoredSession(
        accessToken: 'access-0',
        accessTokenExpiresAtUtc: accessExpired ? clock().subtract(const Duration(minutes: 1)) : clock().add(const Duration(minutes: 10)),
        refreshToken: 'refresh-0',
        refreshTokenExpiresAtUtc: clock().add(const Duration(days: 30)),
        user: CurrentUser.fromJson(Map<String, dynamic>.from(userJson)),
      );
    final client = backend.client;
    final auth = AuthController(api: AuthApi(baseUrl: baseUrl, httpClient: client), store: store, clientName: 'test', clock: clock.call);
    await auth.restore();
    final api = ApiClient(baseUrl: baseUrl, httpClient: client, auth: auth);
    return TestHarness._(backend, store, auth, api, clock);
  }
}

class MutableClock {
  MutableClock(this.value);
  DateTime value;
  DateTime call() => value;
}
