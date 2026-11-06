import 'dart:convert';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import '../models/auth_models.dart';
import '../models/json.dart';

/// Everything needed to resume a signed-in session after the app restarts.
class StoredSession {
  const StoredSession({
    required this.accessToken,
    required this.accessTokenExpiresAtUtc,
    required this.refreshToken,
    required this.refreshTokenExpiresAtUtc,
    required this.user,
    this.institutionCode,
  });

  final String accessToken;
  final DateTime accessTokenExpiresAtUtc;
  final String refreshToken;
  final DateTime refreshTokenExpiresAtUtc;
  final CurrentUser user;

  /// The institution code used at sign-in (only needed when an email exists at
  /// more than one institution). Kept so re-authentication after a password
  /// change can reuse it. Not a secret.
  final String? institutionCode;

  factory StoredSession.fromAuth(AuthResponse r, {String? institutionCode}) => StoredSession(
        accessToken: r.accessToken,
        accessTokenExpiresAtUtc: r.accessTokenExpiresAtUtc,
        refreshToken: r.refreshToken,
        refreshTokenExpiresAtUtc: r.refreshTokenExpiresAtUtc,
        user: r.user,
        institutionCode: institutionCode,
      );

  StoredSession copyWith({CurrentUser? user}) => StoredSession(
        accessToken: accessToken,
        accessTokenExpiresAtUtc: accessTokenExpiresAtUtc,
        refreshToken: refreshToken,
        refreshTokenExpiresAtUtc: refreshTokenExpiresAtUtc,
        user: user ?? this.user,
        institutionCode: institutionCode,
      );

  Json toJson() => {
        'accessToken': accessToken,
        'accessTokenExpiresAtUtc': toUtcIso(accessTokenExpiresAtUtc),
        'refreshToken': refreshToken,
        'refreshTokenExpiresAtUtc': toUtcIso(refreshTokenExpiresAtUtc),
        'user': user.toJson(),
        'institutionCode': institutionCode,
      };

  factory StoredSession.fromJson(Json j) => StoredSession(
        accessToken: j.str('accessToken'),
        accessTokenExpiresAtUtc: j.date('accessTokenExpiresAtUtc'),
        refreshToken: j.str('refreshToken'),
        refreshTokenExpiresAtUtc: j.date('refreshTokenExpiresAtUtc'),
        user: CurrentUser.fromJson(j.obj('user')),
        institutionCode: j.strOrNull('institutionCode'),
      );
}

abstract interface class TokenStore {
  Future<StoredSession?> read();
  Future<void> write(StoredSession session);
  Future<void> clear();
}

/// Tokens live only in the platform keystore (iOS Keychain / Android Keystore-backed
/// encrypted storage) — never in shared preferences or plain files.
class SecureTokenStore implements TokenStore {
  SecureTokenStore([FlutterSecureStorage? storage])
      : _storage = storage ??
            const FlutterSecureStorage(
              iOptions: IOSOptions(accessibility: KeychainAccessibility.first_unlock_this_device),
            );

  static const _key = 'ss.session.v1';
  final FlutterSecureStorage _storage;

  @override
  Future<StoredSession?> read() async {
    try {
      final raw = await _storage.read(key: _key);
      if (raw == null) return null;
      return StoredSession.fromJson(jsonDecode(raw) as Json);
    } on Exception {
      // Corrupt or unreadable entry (e.g. restored backup on a new device): start signed out.
      await clear();
      return null;
    }
  }

  @override
  Future<void> write(StoredSession session) => _storage.write(key: _key, value: jsonEncode(session.toJson()));

  @override
  Future<void> clear() => _storage.delete(key: _key);
}

/// Non-persistent store for tests.
class InMemoryTokenStore implements TokenStore {
  StoredSession? value;

  @override
  Future<StoredSession?> read() async => value;

  @override
  Future<void> write(StoredSession session) async => value = session;

  @override
  Future<void> clear() async => value = null;
}
