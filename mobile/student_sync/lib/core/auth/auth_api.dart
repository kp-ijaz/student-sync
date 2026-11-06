import 'dart:convert';

import 'package:http/http.dart' as http;

import '../api/api_client.dart' show sendRaw;
import '../api/api_exception.dart';
import '../models/auth_models.dart';
import '../models/json.dart';

/// Unauthenticated (or explicitly-authenticated) calls to `/auth` and `/me/password`,
/// including student self-registration.
/// Deliberately separate from [ApiClient] so token renewal never recurses into itself.
class AuthApi {
  AuthApi({required String baseUrl, required http.Client httpClient, this.timeout = const Duration(seconds: 20)})
      : _baseUrl = baseUrl,
        _http = httpClient;

  final String _baseUrl;
  final http.Client _http;
  final Duration timeout;

  Future<AuthResponse> login({required String email, required String password, String? institutionCode, required String clientName}) async {
    final json = await _post('/auth/login', {
      'email': email,
      'password': password,
      'institutionCode': (institutionCode == null || institutionCode.trim().isEmpty) ? null : institutionCode.trim(),
      'clientName': clientName,
    });
    return AuthResponse.fromJson(json! as Json);
  }

  /// Exchanges a refresh token. The server rotates it: the old token must never be
  /// presented again (doing so revokes the whole session family).
  Future<AuthResponse> refresh(String refreshToken) async {
    final json = await _post('/auth/refresh', {'refreshToken': refreshToken});
    return AuthResponse.fromJson(json! as Json);
  }

  Future<void> logout(String refreshToken) async {
    await _post('/auth/logout', {'refreshToken': refreshToken});
  }

  Future<void> changePassword({required String accessToken, required String currentPassword, required String newPassword}) async {
    await _post('/me/password', {'currentPassword': currentPassword, 'newPassword': newPassword}, bearer: accessToken);
  }

  Future<Object?> _post(String path, Object body, {String? bearer}) => _send('POST', path, body: body, bearer: bearer);

  Future<Object?> _send(String method, String path, {Object? body, String? bearer}) async {
    final request = http.Request(method, Uri.parse('$_baseUrl$path'))..headers['Accept'] = 'application/json';
    if (body != null) {
      request.headers['Content-Type'] = 'application/json; charset=utf-8';
      request.body = jsonEncode(body);
    }
    if (bearer != null) request.headers['Authorization'] = 'Bearer $bearer';
    final response = await sendRaw(_http, request, timeout);
    if (response.statusCode >= 200 && response.statusCode < 300) {
      return response.body.isEmpty ? null : jsonDecode(response.body);
    }
    throw ApiException.fromResponse(response.statusCode, response.body);
  }
}
