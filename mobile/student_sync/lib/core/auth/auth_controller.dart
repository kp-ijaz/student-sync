import 'dart:async';

import 'package:flutter/foundation.dart';

import '../api/api_client.dart';
import '../api/api_exception.dart';
import '../models/auth_models.dart';
import 'auth_api.dart';
import 'token_store.dart';

enum AuthStatus { initializing, signedOut, signedIn }

enum SignOutReason { none, userRequested, sessionExpired }

/// Owns the signed-in session: sign-in, serialised token refresh, password-change
/// gate, and sign-out. Router redirects listen to this notifier.
class AuthController extends ChangeNotifier implements AuthTokenSource {
  AuthController({
    required AuthApi api,
    required TokenStore store,
    required this.clientName,
    DateTime Function()? clock,
    this.refreshLeeway = const Duration(seconds: 30),
  })  : _api = api,
        _store = store,
        _clock = clock ?? (() => DateTime.now().toUtc());

  final AuthApi _api;
  final TokenStore _store;
  final DateTime Function() _clock;
  final String clientName;

  /// Renew the access token this long before it expires.
  final Duration refreshLeeway;

  AuthStatus _status = AuthStatus.initializing;
  StoredSession? _session;
  SignOutReason _lastSignOutReason = SignOutReason.none;

  /// The single in-flight refresh. Every caller that needs a new token awaits this
  /// same future, so a rotated refresh token is never presented twice.
  Future<bool>? _refreshInFlight;

  AuthStatus get status => _status;
  CurrentUser? get user => _session?.user;
  bool get isSignedIn => _status == AuthStatus.signedIn && _session != null;
  bool get mustChangePassword => _session?.user.mustChangePassword ?? false;
  SignOutReason get lastSignOutReason => _lastSignOutReason;

  /// Restores a stored session at app start. Does not hit the network.
  Future<void> restore() async {
    final stored = await _store.read();
    if (stored == null || !stored.refreshTokenExpiresAtUtc.isAfter(_clock())) {
      if (stored != null) await _store.clear();
      _session = null;
      _status = AuthStatus.signedOut;
    } else {
      _session = stored;
      _status = AuthStatus.signedIn;
    }
    notifyListeners();
  }

  /// Signs in. Throws [ApiException] — notably `institution_code_required` (400)
  /// when the email exists at more than one institution.
  Future<void> signIn({required String email, required String password, String? institutionCode}) async {
    final response = await _api.login(email: email.trim(), password: password, institutionCode: institutionCode, clientName: clientName);
    await _setSession(StoredSession.fromAuth(response, institutionCode: institutionCode));
    _lastSignOutReason = SignOutReason.none;
    _status = AuthStatus.signedIn;
    notifyListeners();
  }

  /// Changes a (temporary) password. The server revokes every refresh token on
  /// success, so we immediately sign in again with the new password.
  Future<void> changePassword({required String currentPassword, required String newPassword}) async {
    final session = _session;
    if (session == null) throw ApiException.sessionExpired();
    final token = await freshAccessToken();
    if (token == null) throw ApiException.sessionExpired();
    await _api.changePassword(accessToken: token, currentPassword: currentPassword, newPassword: newPassword);
    try {
      await signIn(email: session.user.email, password: newPassword, institutionCode: session.institutionCode);
    } on ApiException {
      // Password changed but re-authentication failed: ask the user to sign in.
      await _expire(SignOutReason.userRequested);
      rethrow;
    }
  }

  /// Replaces the cached user (e.g. after a profile update or `GET /me`).
  Future<void> updateUser(CurrentUser user) async {
    final session = _session;
    if (session == null) return;
    await _setSession(session.copyWith(user: user));
    notifyListeners();
  }

  Future<void> signOut() async {
    final session = _session;
    await _clearLocal(SignOutReason.userRequested);
    if (session != null) {
      // Best effort: revoke the refresh token server-side. Local sign-out has
      // already happened, so a network failure here is not shown to the user.
      try {
        await _api.logout(session.refreshToken);
      } on ApiException {
        // ignore
      }
    }
  }

  // ---------- AuthTokenSource ----------

  @override
  Future<String?> freshAccessToken() async {
    final session = _session;
    if (session == null) return null;
    if (session.accessTokenExpiresAtUtc.isAfter(_clock().add(refreshLeeway))) return session.accessToken;
    final ok = await _refresh();
    return ok ? _session?.accessToken : null;
  }

  @override
  Future<String?> refreshAfterUnauthorized(String? rejectedToken) async {
    final session = _session;
    if (session == null) return null;
    // Another request already renewed the token while this one was in flight.
    if (rejectedToken != null && session.accessToken != rejectedToken) return session.accessToken;
    final ok = await _refresh();
    return ok ? _session?.accessToken : null;
  }

  @override
  void onPasswordChangeRequired() {
    final session = _session;
    if (session == null || session.user.mustChangePassword) return;
    _session = session.copyWith(user: session.user.copyWith(mustChangePassword: true));
    notifyListeners();
  }

  // ---------- internals ----------

  Future<bool> _refresh() {
    return _refreshInFlight ??= _doRefresh().whenComplete(() => _refreshInFlight = null);
  }

  Future<bool> _doRefresh() async {
    final session = _session;
    if (session == null) return false;
    if (!session.refreshTokenExpiresAtUtc.isAfter(_clock())) {
      await _expire(SignOutReason.sessionExpired);
      return false;
    }
    try {
      final response = await _api.refresh(session.refreshToken);
      // Keep the user's institution code; refresh responses carry a fresh user DTO.
      await _setSession(StoredSession.fromAuth(response, institutionCode: session.institutionCode));
      notifyListeners();
      return true;
    } on ApiException catch (e) {
      // Offline or server trouble: keep the session and let the caller show a
      // retryable error. Only a definitive rejection signs the user out.
      if (e.isNetwork || e.isServerError || e.statusCode == 429) rethrow;
      await _expire(SignOutReason.sessionExpired);
      return false;
    }
  }

  Future<void> _setSession(StoredSession session) async {
    _session = session;
    await _store.write(session);
  }

  Future<void> _expire(SignOutReason reason) => _clearLocal(reason);

  Future<void> _clearLocal(SignOutReason reason) async {
    _session = null;
    _status = AuthStatus.signedOut;
    _lastSignOutReason = reason;
    await _store.clear();
    notifyListeners();
  }
}
