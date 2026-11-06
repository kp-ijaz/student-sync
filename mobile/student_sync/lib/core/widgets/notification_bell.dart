import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../app_services.dart';
import '../theme/tokens.dart';
import 'avatar.dart';

/// Actions for the top bar of every tab: notifications and the profile avatar.
/// (Profile is not a tab; it opens from the avatar.)
const tabBarActions = <Widget>[NotificationBell(), SizedBox(width: SsSpace.s2), ProfileButton(), SizedBox(width: SsSpace.s4)];

/// App-bar bell with the unread count. Opens the in-app notification list.
class NotificationBell extends StatelessWidget {
  const NotificationBell({super.key});

  @override
  Widget build(BuildContext context) {
    final unread = AppScope.of(context).unread;
    return ValueListenableBuilder<int>(
      valueListenable: unread,
      builder: (context, count, _) => _CircleButton(
        tooltip: count == 0 ? 'Notifications' : 'Notifications, $count unread',
        onPressed: () => context.push('/notifications'),
        child: Badge(
          isLabelVisible: count > 0,
          backgroundColor: SsColors.error,
          offset: const Offset(6, -6),
          label: Text(count > 99 ? '99+' : '$count'),
          child: const Icon(LucideIcons.bell, size: 20, color: SsColors.navy),
        ),
      ),
    );
  }
}

/// The signed-in user's initials on a brand gradient. Opens Profile.
class ProfileButton extends StatelessWidget {
  const ProfileButton({super.key});

  @override
  Widget build(BuildContext context) {
    final auth = AppScope.of(context).auth;
    return ListenableBuilder(
      listenable: auth,
      builder: (context, _) => Tooltip(
        message: 'Profile',
        child: Semantics(
          button: true,
          label: 'Profile',
          excludeSemantics: true,
          child: InkResponse(
            onTap: () => context.push('/profile'),
            radius: 24,
            child: SizedBox.square(
              dimension: kMinTouchTarget,
              child: Center(child: UserAvatar(name: auth.user?.fullName ?? '', photoUrl: auth.user?.photoUrl, size: 40)),
            ),
          ),
        ),
      ),
    );
  }
}

class _CircleButton extends StatelessWidget {
  const _CircleButton({required this.tooltip, required this.onPressed, required this.child});
  final String tooltip;
  final VoidCallback onPressed;
  final Widget child;

  @override
  Widget build(BuildContext context) => IconButton(
        tooltip: tooltip,
        onPressed: onPressed,
        style: IconButton.styleFrom(
          backgroundColor: SsColors.white,
          fixedSize: const Size.square(kMinTouchTarget),
          side: const BorderSide(color: SsColors.hairline),
        ),
        icon: child,
      );
}
