import 'dart:io' show Platform;

import 'package:flutter/foundation.dart';

/// Environment configuration. The API base URL is supplied at build time:
///
///   flutter run --dart-define=API_BASE_URL=https://api.example.com/api/v1
///
/// When it is not supplied, a local development default is used: the Android
/// emulator reaches the host machine through 10.0.2.2, the iOS simulator through
/// localhost. Release builds must always pass API_BASE_URL explicitly.
class AppConfig {
  const AppConfig({required this.apiBaseUrl, required this.clientName});

  /// Base URL including the `/api/v1` prefix, without a trailing slash.
  final String apiBaseUrl;

  /// Sent with sign-in so users can recognise this device in their session list.
  final String clientName;

  static const _definedBaseUrl = String.fromEnvironment('API_BASE_URL');

  static AppConfig fromEnvironment() {
    final base = _definedBaseUrl.isNotEmpty ? _definedBaseUrl : _devDefault();
    return AppConfig(apiBaseUrl: normaliseBaseUrl(base), clientName: _clientName());
  }

  /// Scheme + host + port of the API, used to resolve server-relative links such
  /// as `/api/v1/files/download?token=...`.
  Uri get origin {
    final u = Uri.parse(apiBaseUrl);
    return Uri(scheme: u.scheme, host: u.host, port: u.hasPort ? u.port : null);
  }

  Uri resolveServerPath(String pathOrUrl) {
    final parsed = Uri.parse(pathOrUrl);
    return parsed.hasScheme ? parsed : origin.resolveUri(parsed);
  }

  static String normaliseBaseUrl(String value) =>
      value.endsWith('/') ? value.substring(0, value.length - 1) : value;

  static String _devDefault() {
    if (!kIsWeb && Platform.isAndroid) return 'http://10.0.2.2:5080/api/v1';
    return 'http://localhost:5080/api/v1';
  }

  static String _clientName() {
    if (kIsWeb) return 'Student Sync web';
    if (Platform.isAndroid) return 'Student Sync Android';
    if (Platform.isIOS) return 'Student Sync iOS';
    return 'Student Sync mobile';
  }
}
