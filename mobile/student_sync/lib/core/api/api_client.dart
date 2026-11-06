import 'dart:async';
import 'dart:convert';
import 'dart:io' show SocketException, HandshakeException;
import 'dart:math' show Random;

import 'package:http/http.dart' as http;
import 'package:http_parser/http_parser.dart' show MediaType;

import '../models/json.dart';
import 'api_exception.dart';

/// What the API client needs from the authentication layer. Implemented by
/// AuthController; kept as an interface so the client can be unit tested.
abstract interface class AuthTokenSource {
  /// A non-expired access token, refreshing first if it is about to expire.
  /// Returns null when there is no signed-in session.
  Future<String?> freshAccessToken();

  /// Called after the server rejected [rejectedToken] with 401. Implementations
  /// must serialise refreshes so concurrent callers share one refresh request
  /// (refresh tokens are single-use). Returns the new token, or null when the
  /// session could not be renewed (the user must sign in again).
  Future<String?> refreshAfterUnauthorized(String? rejectedToken);

  /// The server says this account must change its temporary password first.
  void onPasswordChangeRequired();
}

/// The single typed HTTP client for `/api/v1`. Adds the bearer token, renews it
/// once on 401 and retries, and maps failures to [ApiException].
class ApiClient {
  ApiClient({
    required String baseUrl,
    required http.Client httpClient,
    required AuthTokenSource auth,
    this.timeout = const Duration(seconds: 20),
  })  : _baseUrl = baseUrl,
        _http = httpClient,
        _auth = auth;

  final String _baseUrl;
  final http.Client _http;
  final AuthTokenSource _auth;
  final Duration timeout;

  Future<T> get<T>(String path, T Function(Object? json) parse, {Map<String, Object?>? query}) async =>
      parse(await send('GET', path, query: query));

  Future<Object?> post(String path, {Object? body, Map<String, String>? headers}) => send('POST', path, body: body, headers: headers);

  Future<Object?> put(String path, {Object? body, Map<String, String>? headers}) => send('PUT', path, body: body, headers: headers);

  Future<Object?> delete(String path) => send('DELETE', path);

  Uri buildUri(String path, [Map<String, Object?>? query]) {
    final q = <String, String>{};
    query?.forEach((k, v) {
      if (v == null) return;
      q[k] = v is DateTime ? toUtcIso(v) : v.toString();
    });
    final uri = Uri.parse('$_baseUrl$path');
    return q.isEmpty ? uri : uri.replace(queryParameters: {...uri.queryParameters, ...q});
  }

  /// Sends a request and returns the decoded JSON body (null for empty/204).
  Future<Object?> send(String method, String path, {Object? body, Map<String, Object?>? query, Map<String, String>? headers}) =>
      _dispatch((token) => _sendOnce(method, buildUri(path, query), token, body, headers));

  /// Uploads one file as `multipart/form-data` (field [field]) and returns the decoded JSON body.
  Future<Object?> sendFile(String method, String path,
          {String field = 'file', required List<int> bytes, required String filename, required String contentType}) =>
      sendMultipart(method, path, file: () async => http.MultipartFile.fromBytes(field, bytes, filename: filename, contentType: _mediaType(contentType)));

  /// Multipart upload with extra form [fields]. [file] builds a fresh part per attempt
  /// (a part can only be sent once). Streams from disk for large files; [timeout]
  /// should allow for slow uploads.
  Future<Object?> sendMultipart(String method, String path,
          {required Future<http.MultipartFile> Function() file, Map<String, String> fields = const {}, Duration? timeout}) =>
      _dispatch((token) async {
        final request = http.MultipartRequest(method, buildUri(path))
          ..headers['Accept'] = 'application/json'
          ..headers['Authorization'] = 'Bearer $token'
          ..fields.addAll(fields)
          ..files.add(await file());
        return sendRaw(_http, request, timeout ?? this.timeout);
      });

  /// A file part read from disk as it is sent.
  static Future<http.MultipartFile> filePart(String path, {String field = 'file', required String filename, required String contentType}) =>
      http.MultipartFile.fromPath(field, path, filename: filename, contentType: _mediaType(contentType));

  /// Adds the bearer token, renews it once on 401 and retries, then decodes the
  /// body or maps the failure. [once] builds a fresh request for each attempt.
  Future<Object?> _dispatch(Future<http.Response> Function(String token) once) async {
    final token = await _auth.freshAccessToken();
    if (token == null) throw ApiException.sessionExpired();

    var response = await once(token);
    if (response.statusCode == 401) {
      final renewed = await _auth.refreshAfterUnauthorized(token);
      if (renewed == null) throw ApiException.sessionExpired();
      response = await once(renewed);
      if (response.statusCode == 401) throw ApiException.sessionExpired();
    }

    if (response.statusCode >= 200 && response.statusCode < 300) {
      if (response.statusCode == 204 || response.body.isEmpty) return null;
      try {
        return jsonDecode(response.body);
      } on FormatException {
        throw const ApiException(statusCode: 200, code: 'invalid_response', message: 'The server sent a response we could not read.');
      }
    }

    final error = ApiException.fromResponse(response.statusCode, response.body);
    if (error.code == 'password_change_required') _auth.onPasswordChangeRequired();
    throw error;
  }

  static MediaType _mediaType(String contentType) {
    final parts = contentType.split('/');
    return MediaType(parts.first, parts.length > 1 ? parts[1] : 'octet-stream');
  }

  Future<http.Response> _sendOnce(String method, Uri uri, String token, Object? body, Map<String, String>? extraHeaders) async {
    final request = http.Request(method, uri);
    request.headers['Accept'] = 'application/json';
    request.headers['Authorization'] = 'Bearer $token';
    if (extraHeaders != null) request.headers.addAll(extraHeaders);
    if (body != null) {
      request.headers['Content-Type'] = 'application/json; charset=utf-8';
      request.body = jsonEncode(body);
    }
    return sendRaw(_http, request, timeout);
  }
}

/// Executes [request] and converts transport failures to [ApiException.network].
Future<http.Response> sendRaw(http.Client client, http.BaseRequest request, Duration timeout) async {
  try {
    final streamed = await client.send(request).timeout(timeout);
    return await http.Response.fromStream(streamed).timeout(timeout);
  } on TimeoutException {
    throw ApiException.network();
  } on SocketException {
    throw ApiException.network();
  } on HandshakeException {
    throw ApiException.network();
  } on http.ClientException {
    throw ApiException.network();
  }
}

/// Random 128-bit hex key for the server's `Idempotency-Key` header, so a retried
/// create/submit is not applied twice.
String newIdempotencyKey() =>
    List.generate(16, (_) => _secureRandom.nextInt(256).toRadixString(16).padLeft(2, '0')).join();

final _secureRandom = Random.secure();
