import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../app_services.dart';
import '../theme/tokens.dart';
import 'avatar.dart';

/// Home header: the user's photo (opens Profile), a greeting and their first name.
class HomeGreeting extends StatelessWidget {
  const HomeGreeting({super.key});

  static String greetingFor(int hour) => hour < 5 ? 'Good evening' : hour < 12 ? 'Good morning' : hour < 17 ? 'Good afternoon' : 'Good evening';

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    return ListenableBuilder(
      listenable: services.auth,
      builder: (context, _) {
        final user = services.auth.user;
        final greeting = greetingFor(services.time.inViewerZone(DateTime.now().toUtc()).hour);
        return Row(mainAxisSize: MainAxisSize.min, children: [
          Semantics(
            button: true,
            label: 'Profile',
            excludeSemantics: true,
            child: Tooltip(
              message: 'Profile',
              child: InkResponse(
                onTap: () => context.push('/profile'),
                radius: 28,
                child: UserAvatar(name: user?.fullName ?? '', photoUrl: user?.photoUrl, size: 50),
              ),
            ),
          ),
          const SizedBox(width: SsSpace.s3),
          Flexible(
            child: Semantics(
              header: true,
              label: '$greeting, ${user?.firstName ?? ''}',
              excludeSemantics: true,
              child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(greeting, style: const TextStyle(fontFamily: 'Inter', fontSize: 13, height: 18 / 13, fontWeight: FontWeight.w500, color: SsColors.textSecondary)),
                Text(user?.firstName ?? 'Welcome', overflow: TextOverflow.ellipsis),
              ]),
            ),
          ),
        ]);
      },
    );
  }
}
