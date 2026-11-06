import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/app_services.dart';
import '../../core/theme/status_styles.dart';
import '../../core/theme/tokens.dart';
import '../../core/routing/router.dart' show homeFor;
import '../../core/widgets/brand.dart';
import '../../core/widgets/illustration.dart';
import '../../core/widgets/states.dart';
import 'auth_shell.dart' show brandHeroTag;

/// Shown while the stored session is restored. The same soft white as the native launch
/// screen, with the mark's star turning while the app starts; the wordmark fades in
/// beneath and the mark glides into the next screen.
class SplashScreen extends StatelessWidget {
  const SplashScreen({super.key});

  static const _background = Color(0xFFF6F7FE);

  @override
  Widget build(BuildContext context) => AnnotatedRegion<SystemUiOverlayStyle>(
    value: SystemUiOverlayStyle.dark,
    child: Scaffold(
      backgroundColor: _background,
      body: SafeArea(
        child: Semantics(
          label: 'Student Sync is starting',
          child: Column(children: [
            const Spacer(flex: 3),
            const Hero(tag: brandHeroTag, child: BrandSpinner(size: 112, label: 'Starting')),
            const SizedBox(height: SsSpace.s5),
            TweenAnimationBuilder<double>(
              tween: Tween(begin: 0, end: 1),
              duration: const Duration(milliseconds: 600),
              curve: Curves.easeOutCubic,
              builder: (context, v, child) => Opacity(
                opacity: v,
                child: Transform.translate(offset: Offset(0, 10 * (1 - v)), child: child),
              ),
              child: const ExcludeSemantics(child: BrandWordmark(height: 36)),
            ),
            const Spacer(flex: 4),
            const ExcludeSemantics(
              child: Padding(
                padding: EdgeInsets.only(bottom: SsSpace.s5),
                child: Text('Your classes, teachers and progress', style: TextStyle(fontFamily: 'Inter', fontSize: 13, color: SsColors.textSecondary)),
              ),
            ),
          ]),
        ),
      ),
    ),
  );
}

/// Shown for links the app does not recognise (old notifications, mistyped deep links).
class NotFoundScreen extends StatelessWidget {
  const NotFoundScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final auth = AppScope.of(context).auth;
    final role = auth.user?.role;
    return Scaffold(
      appBar: AppBar(),
      body: EmptyState(
        illustration: Illustration.notFound,
        icon: LucideIcons.searchX,
        title: "We couldn't find that page",
        message: 'The link may be old or mistyped. Everything you need is on your home screen.',
        action: FilledButton.icon(
          onPressed: () => context.go(auth.isSignedIn && role != null ? homeFor(role) : '/login'),
          icon: const Icon(LucideIcons.house, size: 18),
          label: const Text('Go to home'),
        ),
      ),
    );
  }
}

/// Administrators manage the institute from the web app; the mobile app covers
/// students and teachers.
class WebPortalScreen extends StatelessWidget {
  const WebPortalScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    final user = services.auth.user;
    final t = Theme.of(context).textTheme;
    return Scaffold(
      appBar: AppBar(title: const BrandAppBarTitle()),
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(SsSpace.s5),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const ExcludeSemantics(child: Icon(LucideIcons.monitor, size: 48, color: SsColors.blue)),
                const SizedBox(height: SsSpace.s4),
                Semantics(
                  header: true,
                  child: Text('Please use the web portal', style: t.headlineSmall, textAlign: TextAlign.center),
                ),
                const SizedBox(height: SsSpace.s2),
                Text(
                  'You are signed in as ${user?.fullName ?? 'an administrator'}'
                  '${user == null ? '' : ' (${StatusStyles.roleLabel(user.role)})'}. '
                  'Administration tools are available in the Student Sync web app. '
                  'The mobile app is for students and teachers.',
                  style: t.bodyMedium,
                  textAlign: TextAlign.center,
                ),
                const SizedBox(height: SsSpace.s5),
                OutlinedButton(onPressed: services.signOut, child: const Text('Sign out')),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
