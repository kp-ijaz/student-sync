import 'enums.dart';
import 'json.dart';

class CurrentUser {
  const CurrentUser({
    required this.id,
    required this.fullName,
    required this.email,
    required this.role,
    required this.tenantId,
    required this.tenantName,
    required this.timeZoneId,
    required this.preferredLanguage,
    required this.mustChangePassword,
    required this.permissions,
    required this.enabledFeatures,
    this.photoUrl,
  });

  final String id;
  final String fullName;
  final String email;
  final UserRole role;
  final String? tenantId;
  final String? tenantName;
  final String timeZoneId;
  final String preferredLanguage;
  final bool mustChangePassword;
  final List<String> permissions;
  final List<String> enabledFeatures;

  /// Short-lived link to the profile photo (server path), or null for initials.
  final String? photoUrl;

  String get firstName => fullName.split(' ').first;
  bool can(String permission) => permissions.contains(permission);
  bool hasFeature(String feature) => enabledFeatures.contains(feature);

  factory CurrentUser.fromJson(Json j) => CurrentUser(
        id: j.str('id'),
        fullName: j.str('fullName'),
        email: j.str('email'),
        role: j.enumValue('role', UserRole.values),
        tenantId: j.strOrNull('tenantId'),
        tenantName: j.strOrNull('tenantName'),
        timeZoneId: j.str('timeZoneId'),
        preferredLanguage: j.str('preferredLanguage'),
        mustChangePassword: j.boolean('mustChangePassword'),
        permissions: j.strings('permissions'),
        enabledFeatures: j.strings('enabledFeatures'),
        photoUrl: j.strOrNull('photoUrl'),
      );

  Json toJson() => {
        'id': id,
        'fullName': fullName,
        'email': email,
        'role': enumToJson(role),
        'tenantId': tenantId,
        'tenantName': tenantName,
        'timeZoneId': timeZoneId,
        'preferredLanguage': preferredLanguage,
        'mustChangePassword': mustChangePassword,
        'permissions': permissions,
        'enabledFeatures': enabledFeatures,
        'photoUrl': photoUrl,
      };

  CurrentUser copyWith({bool? mustChangePassword}) => CurrentUser(
        id: id,
        fullName: fullName,
        email: email,
        role: role,
        tenantId: tenantId,
        tenantName: tenantName,
        timeZoneId: timeZoneId,
        preferredLanguage: preferredLanguage,
        mustChangePassword: mustChangePassword ?? this.mustChangePassword,
        permissions: permissions,
        enabledFeatures: enabledFeatures,
        photoUrl: photoUrl,
      );
}

class AuthResponse {
  const AuthResponse({
    required this.accessToken,
    required this.accessTokenExpiresAtUtc,
    required this.refreshToken,
    required this.refreshTokenExpiresAtUtc,
    required this.user,
  });

  final String accessToken;
  final DateTime accessTokenExpiresAtUtc;
  final String refreshToken;
  final DateTime refreshTokenExpiresAtUtc;
  final CurrentUser user;

  factory AuthResponse.fromJson(Json j) => AuthResponse(
        accessToken: j.str('accessToken'),
        accessTokenExpiresAtUtc: j.date('accessTokenExpiresAtUtc'),
        refreshToken: j.str('refreshToken'),
        refreshTokenExpiresAtUtc: j.date('refreshTokenExpiresAtUtc'),
        user: CurrentUser.fromJson(j.obj('user')),
      );
}
