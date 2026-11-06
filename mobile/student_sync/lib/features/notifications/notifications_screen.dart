import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/app_services.dart';
import '../../core/models/common.dart';
import '../../core/models/content_models.dart';
import '../../core/models/enums.dart';
import '../../core/theme/tokens.dart';
import '../../core/widgets/async_view.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/illustration.dart';
import '../../core/widgets/states.dart';

/// Maps a server notification link (web path) to an in-app route for [role], or
/// null when the app has no suitable screen for it.
String? inAppRouteFor(String? link, UserRole role) {
  if (link == null) return null;
  final uri = Uri.tryParse(link);
  if (uri == null) return null;
  final seg = uri.pathSegments.where((s) => s.isNotEmpty).toList();
  if (seg.length >= 2) {
    final id = seg[1];
    final guid = RegExp(r'^[0-9a-fA-F-]{36}$');
    if (!guid.hasMatch(id)) return null;
    switch (seg[0]) {
      case 'doubts' when role != UserRole.parent:
        return '/doubts/$id';
      case 'assignments' when role == UserRole.student:
        return '/assignments/$id';
      case 'assignments' when role == UserRole.teacher:
        return '/teacher/tasks';
      case 'sessions' || 'classes':
        return '/sessions/$id';
    }
  }
  return null;
}

class NotificationsScreen extends StatefulWidget {
  const NotificationsScreen({super.key});

  @override
  State<NotificationsScreen> createState() => _NotificationsScreenState();
}

class _NotificationsScreenState extends State<NotificationsScreen> {
  int _version = 0;
  bool _busy = false;

  Future<void> _markAll() async {
    setState(() => _busy = true);
    final services = AppScope.read(context);
    try {
      await services.profile.markAllRead();
      await services.unread.refresh();
      if (mounted) setState(() => _version++);
    } on Object catch (e) {
      if (mounted) showSnack(context, describeError(e), error: true);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _open(AppNotification n) async {
    final services = AppScope.read(context);
    if (!n.isRead) {
      try {
        await services.profile.markRead(n.id);
        await services.unread.refresh();
        if (mounted) setState(() => _version++);
      } on Object catch (e) {
        if (mounted) showSnack(context, describeError(e), error: true);
        return;
      }
    }
    final route = inAppRouteFor(n.link, services.auth.user!.role);
    if (route != null && mounted) await context.push(route);
  }

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    return Scaffold(
      appBar: AppBar(
        title: const Text('Notifications'),
        actions: [
          ValueListenableBuilder<int>(
            valueListenable: services.unread,
            builder: (context, count, _) => TextButton(onPressed: count == 0 || _busy ? null : _markAll, child: const Text('Mark all read')),
          ),
        ],
      ),
      body: AsyncView<PagedResult<AppNotification>>(
        key: ValueKey(_version),
        load: () async {
          final page = await services.profile.notifications(pageSize: 50);
          await services.unread.refresh();
          return page;
        },
        isEmpty: (p) => p.items.isEmpty,
        empty: (context, _) => const EmptyState(icon: LucideIcons.bellRing, illustration: Illustration.mailbox, title: 'No notifications', message: 'Updates about your classes, work and doubts will appear here.'),
        builder: (context, page, reload) {
          final t = Theme.of(context).textTheme;
          final time = services.time;
          return RefreshIndicator(
            onRefresh: reload,
            child: ListView.separated(
              padding: pagePadding(context).copyWith(top: SsSpace.s3),
              itemCount: page.items.length,
              separatorBuilder: (_, _) => const SizedBox(height: SsSpace.s2),
              itemBuilder: (context, i) {
                final n = page.items[i];
                final route = inAppRouteFor(n.link, services.auth.user!.role);
                return SsCard(
                  onTap: (!n.isRead || route != null) ? () => _open(n) : null,
                  color: n.isRead ? null : SsColors.blue50,
                  semanticLabel: '${n.isRead ? '' : 'Unread. '}${n.title}',
                  child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                    Badge(
                      isLabelVisible: !n.isRead,
                      smallSize: 10,
                      backgroundColor: SsColors.blue,
                      child: IconTile(LucideIcons.bell, color: n.isRead ? SsColors.slate : SsColors.blue, background: n.isRead ? SsColors.fill : SsColors.white),
                    ),
                    const SizedBox(width: SsSpace.s3),
                    Expanded(
                      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                        Text(n.title, style: t.titleSmall),
                        const SizedBox(height: SsSpace.s1),
                        Text(n.body, style: t.bodyMedium),
                        const SizedBox(height: SsSpace.s1),
                        Text(time.dateTime(n.createdAtUtc), style: t.labelSmall),
                      ]),
                    ),
                    if (route != null) const ExcludeSemantics(child: Icon(LucideIcons.chevronRight, size: 18, color: SsColors.slate)),
                  ]),
                );
              },
            ),
          );
        },
      ),
    );
  }
}
