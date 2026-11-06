import 'dart:io' show Platform;

import 'package:flutter/foundation.dart';

import '../api/api_client.dart';
import '../api/api_exception.dart';

/// Source of a platform push token (FCM on Android, APNs on iOS).
///
/// No push credentials exist for this project yet, so the shipped implementation is
/// [UnconfiguredPushTokenProvider]: it reports that push is unavailable and the app
/// relies on in-app notifications (`GET /notifications`). A real provider (e.g. backed
/// by firebase_messaging) can be dropped in without touching the rest of the app.
abstract interface class PushTokenProvider {
  /// True when this build can obtain device tokens.
  bool get isConfigured;

  /// Requests permission (if needed) and returns the current token, or null.
  Future<String?> obtainToken();
}

class UnconfiguredPushTokenProvider implements PushTokenProvider {
  const UnconfiguredPushTokenProvider();

  @override
  bool get isConfigured => false;

  @override
  Future<String?> obtainToken() async => null;
}

/// Registers this device with `POST /devices` when a push token is available, and
/// unregisters it on sign-out. Never claims success it did not get from the API.
class DeviceRegistrationService {
  DeviceRegistrationService({required ApiClient api, required PushTokenProvider provider})
      : _api = api,
        _provider = provider;

  final ApiClient _api;
  final PushTokenProvider _provider;
  String? _registeredToken;

  bool get pushConfigured => _provider.isConfigured;

  /// Returns true only when the server accepted the registration.
  Future<bool> registerIfAvailable() async {
    if (!_provider.isConfigured) return false;
    final token = await _provider.obtainToken();
    if (token == null || token.isEmpty) return false;
    try {
      await _api.post('/devices', body: {'platform': _platformName(), 'token': token});
      _registeredToken = token;
      return true;
    } on ApiException catch (e) {
      debugPrint('Device registration failed: ${e.code}');
      return false;
    }
  }

  Future<void> unregister() async {
    final token = _registeredToken;
    if (token == null) return;
    try {
      await _api.delete('/devices/${Uri.encodeComponent(token)}');
    } on ApiException {
      // Best effort during sign-out.
    }
    _registeredToken = null;
  }

  static String _platformName() {
    if (!kIsWeb && Platform.isIOS) return 'ios';
    if (!kIsWeb && Platform.isAndroid) return 'android';
    return 'web';
  }
}
