import 'dart:convert';

/// A failed API call, mapped from the server's RFC 7807 problem JSON
/// (`{title, status, code, errors?}`) or from a transport failure.
class ApiException implements Exception {
  const ApiException({
    required this.statusCode,
    required this.code,
    required this.message,
    this.fieldErrors = const {},
  });

  /// Null when the request never produced an HTTP response (offline, timeout, DNS…).
  final int? statusCode;

  /// Stable machine-readable code from the server, e.g. `validation_failed`,
  /// `forbidden`, `institution_code_required`, `password_change_required`.
  final String code;

  /// Human-readable message safe to show to the user.
  final String message;

  /// Validation errors keyed by camelCase field name.
  final Map<String, List<String>> fieldErrors;

  factory ApiException.network() => const ApiException(
        statusCode: null,
        code: 'network_error',
        message: "We couldn't reach Student Sync. Check your connection and try again.",
      );

  factory ApiException.sessionExpired() => const ApiException(
        statusCode: 401,
        code: 'session_expired',
        message: 'Your session has expired. Please sign in again.',
      );

  bool get isNetwork => statusCode == null;
  bool get isUnauthorized => statusCode == 401;
  bool get isForbidden => statusCode == 403;
  bool get isNotFound => statusCode == 404;
  bool get isConflict => statusCode == 409;
  bool get isValidation => code == 'validation_failed';
  bool get isServerError => (statusCode ?? 0) >= 500;

  /// First validation message for [field] (case-insensitive), if any.
  String? fieldError(String field) {
    for (final e in fieldErrors.entries) {
      if (e.key.toLowerCase() == field.toLowerCase() && e.value.isNotEmpty) return e.value.first;
    }
    return null;
  }

  /// Builds an exception from an HTTP error response body (problem JSON when possible).
  factory ApiException.fromResponse(int statusCode, String body) {
    Map<String, dynamic>? json;
    if (body.isNotEmpty) {
      try {
        final decoded = jsonDecode(body);
        if (decoded is Map<String, dynamic>) json = decoded;
      } on FormatException {
        json = null;
      }
    }
    final code = json?['code'] as String? ?? _defaultCode(statusCode);
    final title = json?['title'] as String? ?? json?['detail'] as String?;
    final errors = <String, List<String>>{};
    final rawErrors = json?['errors'];
    if (rawErrors is Map<String, dynamic>) {
      for (final e in rawErrors.entries) {
        final v = e.value;
        errors[e.key] = v is List ? v.map((x) => x.toString()).toList() : [v.toString()];
      }
    }
    return ApiException(
      statusCode: statusCode,
      code: code,
      message: (title == null || title.isEmpty) ? _defaultMessage(statusCode) : title,
      fieldErrors: errors,
    );
  }

  static String _defaultCode(int status) => switch (status) {
        400 => 'bad_request',
        401 => 'unauthenticated',
        403 => 'forbidden',
        404 => 'not_found',
        409 => 'conflict',
        429 => 'rate_limited',
        _ when status >= 500 => 'server_error',
        _ => 'http_$status',
      };

  static String _defaultMessage(int status) => switch (status) {
        401 => 'Please sign in to continue.',
        403 => 'You do not have permission to view this.',
        404 => 'We could not find what you were looking for.',
        429 => 'Too many attempts. Please wait a moment and try again.',
        _ when status >= 500 => 'Something went wrong on our side. Please try again.',
        _ => 'The request could not be completed.',
      };

  @override
  String toString() => 'ApiException($statusCode, $code): $message';
}
