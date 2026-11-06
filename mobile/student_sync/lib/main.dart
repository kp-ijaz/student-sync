import 'package:flutter/material.dart';
import 'package:timezone/data/latest_all.dart' as tzdata;

import 'app.dart';
import 'core/app_services.dart';
import 'core/auth/welcome_store.dart';
import 'core/config/app_config.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  // Full IANA database: sessions and users can be in any zone, and DST rules matter.
  tzdata.initializeTimeZones();

  final services = AppServices.create(config: AppConfig.fromEnvironment(), welcomeStore: SecureWelcomeStore());
  await Future.wait([services.auth.restore(), services.welcome.load()]);

  runApp(StudentSyncApp(services: services));
}
