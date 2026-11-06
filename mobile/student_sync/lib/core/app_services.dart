import 'package:flutter/widgets.dart';
import 'package:http/http.dart' as http;

import '../features/doubts/doubts_api.dart';
import '../features/learning/learning_api.dart';
import '../features/profile/profile_api.dart';
import '../features/sessions/sessions_api.dart';
import '../features/student/dashboard_api.dart';
import '../features/subjects/subjects_api.dart';
import '../features/videos/videos_api.dart';
import 'api/api_client.dart';
import 'api/api_exception.dart';
import 'auth/auth_api.dart';
import 'auth/auth_controller.dart';
import 'auth/token_store.dart';
import 'auth/welcome_store.dart';
import 'config/app_config.dart';
import 'push/push_registration.dart';
import 'time/time_format.dart';

/// Composition root: one instance of each service for the app's lifetime.
class AppServices {
  AppServices._({
    required this.config,
    required this.auth,
    required this.authApi,
    required this.api,
    required this.devices,
    required this.welcome,
  })  : sessions = SessionsApi(api),
        videos = VideosApi(api),
        learning = LearningApi(api),
        doubts = DoubtsApi(api),
        dashboards = DashboardApi(api),
        subjects = SubjectsApi(api),
        profile = ProfileApi(api),
        unread = UnreadNotifications(ProfileApi(api)),
        parentChild = ValueNotifier<String?>(null);

  factory AppServices.create({
    required AppConfig config,
    http.Client? httpClient,
    TokenStore? tokenStore,
    WelcomeStore? welcomeStore,
    PushTokenProvider pushProvider = const UnconfiguredPushTokenProvider(),
  }) {
    final client = httpClient ?? http.Client();
    final authApi = AuthApi(baseUrl: config.apiBaseUrl, httpClient: client);
    final auth = AuthController(
      api: authApi,
      store: tokenStore ?? SecureTokenStore(),
      clientName: config.clientName,
    );
    final api = ApiClient(baseUrl: config.apiBaseUrl, httpClient: client, auth: auth);
    return AppServices._(
      config: config,
      auth: auth,
      authApi: authApi,
      api: api,
      devices: DeviceRegistrationService(api: api, provider: pushProvider),
      // Tests and other callers that pass no store skip the welcome screens.
      welcome: WelcomeState(welcomeStore ?? InMemoryWelcomeStore()),
    );
  }

  final AppConfig config;
  final AuthController auth;

  /// Anonymous `/auth` calls that need no session.
  final AuthApi authApi;
  final ApiClient api;
  final DeviceRegistrationService devices;

  /// Whether this device has seen the welcome screens.
  final WelcomeState welcome;
  final SessionsApi sessions;
  final VideosApi videos;
  final LearningApi learning;
  final DoubtsApi doubts;
  final DashboardApi dashboards;
  final SubjectsApi subjects;
  final ProfileApi profile;
  final UnreadNotifications unread;

  /// Parent role: the child currently selected in the child switcher (shared by tabs).
  final ValueNotifier<String?> parentChild;

  TimeFormatter get time => TimeFormatter(auth.user?.timeZoneId ?? 'UTC');

  Future<void> signOut() async {
    await devices.unregister();
    parentChild.value = null;
    unread.reset();
    await auth.signOut();
  }
}

/// Unread notification count for the bell badge. Polled on app resume and on
/// pull-to-refresh (no push delivery exists yet).
class UnreadNotifications extends ValueNotifier<int> {
  UnreadNotifications(this._api) : super(0);
  final ProfileApi _api;

  Future<void> refresh() async {
    try {
      value = (await _api.unread()).notifications;
    } on ApiException {
      // Leave the last known count; the badge is advisory.
    }
  }

  void reset() => value = 0;
}

class AppScope extends InheritedWidget {
  const AppScope({super.key, required this.services, required super.child});

  final AppServices services;

  static AppServices of(BuildContext context) {
    final scope = context.dependOnInheritedWidgetOfExactType<AppScope>();
    assert(scope != null, 'AppScope not found in widget tree');
    return scope!.services;
  }

  /// Read without subscribing (for callbacks).
  static AppServices read(BuildContext context) {
    final scope = context.getInheritedWidgetOfExactType<AppScope>();
    assert(scope != null, 'AppScope not found in widget tree');
    return scope!.services;
  }

  @override
  bool updateShouldNotify(AppScope oldWidget) => oldWidget.services != services;
}
