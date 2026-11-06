import 'dart:async';

import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import 'core/api/api_exception.dart';
import 'core/app_services.dart';
import 'core/routing/router.dart';
import 'core/theme/app_theme.dart';

class StudentSyncApp extends StatefulWidget {
  const StudentSyncApp({super.key, required this.services});
  final AppServices services;

  @override
  State<StudentSyncApp> createState() => _StudentSyncAppState();
}

class _StudentSyncAppState extends State<StudentSyncApp> with WidgetsBindingObserver {
  late final GoRouter _router = buildRouter(widget.services.auth, widget.services.welcome);
  bool _wasSignedIn = false;

  AppServices get _s => widget.services;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _s.auth.addListener(_onAuthChanged);
    _onAuthChanged();
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _s.auth.removeListener(_onAuthChanged);
    _router.dispose();
    super.dispose();
  }

  /// On every transition into a signed-in state: refresh the profile, the unread
  /// badge, and (if push is configured) the device registration.
  void _onAuthChanged() {
    final signedIn = _s.auth.isSignedIn && !_s.auth.mustChangePassword;
    if (signedIn && !_wasSignedIn) {
      unawaited(_afterSignIn());
    }
    _wasSignedIn = signedIn;
  }

  Future<void> _afterSignIn() async {
    try {
      await _s.auth.updateUser(await _s.profile.me());
    } on ApiException {
      // Offline at start: keep the cached profile.
    }
    await _s.unread.refresh();
    await _s.devices.registerIfAvailable();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed && _s.auth.isSignedIn && !_s.auth.mustChangePassword) {
      unawaited(_s.unread.refresh());
    }
  }

  @override
  Widget build(BuildContext context) {
    return AppScope(
      services: _s,
      child: ListenableBuilder(
        // Rebuild when the user's time zone (or identity) changes so every screen
        // re-renders times in the right zone.
        listenable: _s.auth,
        builder: (context, _) => MaterialApp.router(
          key: ValueKey('${_s.auth.user?.id}/${_s.auth.user?.timeZoneId}'),
          title: 'Student Sync',
          debugShowCheckedModeBanner: false,
          theme: buildAppTheme(),
          routerConfig: _router,
        ),
      ),
    );
  }
}
