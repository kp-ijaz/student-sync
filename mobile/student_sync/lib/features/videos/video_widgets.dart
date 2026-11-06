import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/app_services.dart';
import '../../core/models/common.dart';
import '../../core/models/content_models.dart';
import '../../core/models/enums.dart';
import '../../core/theme/status_styles.dart';
import '../../core/theme/tokens.dart';
import '../../core/time/time_format.dart';
import '../../core/widgets/async_view.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/states.dart';

/// "30 minutes", "1 hour", "2 days", "1 week" — the same labels the web picker uses.
String unlockOffsetLabel(int minutes) {
  if (minutes % 10080 == 0) return minutes == 10080 ? '1 week' : '${minutes ~/ 10080} weeks';
  if (minutes % 1440 == 0) return minutes == 1440 ? '1 day' : '${minutes ~/ 1440} days';
  if (minutes % 60 == 0) return minutes == 60 ? '1 hour' : '${minutes ~/ 60} hours';
  return '$minutes minutes';
}

/// "45 sec" for clips under a minute (where "0 min" would mislead), otherwise "12 min" / "1 h 5 min".
String videoLengthLabel(int seconds) => seconds < 60 ? '$seconds sec' : formatDuration(Duration(seconds: seconds));

/// The automatic rule in words, e.g. "Opens 1 hour before the class".
String unlockRuleLabel(VideoUnlockRule rule, int offsetMinutes) {
  final when = offsetMinutes == 0 ? null : unlockOffsetLabel(offsetMinutes);
  return rule == VideoUnlockRule.beforeClass
      ? (when == null ? 'Opens when the class starts' : 'Opens $when before the class')
      : (when == null ? 'Opens when the class ends' : 'Opens $when after the class');
}

/// The line under a video's title explaining its lock state. Mirrors the web list.
String videoLockDetail(PrerecordedVideo v, TimeFormatter time) {
  if (v.unlocksAtUtc != null) return 'Unlocks ${time.dateTime(v.unlocksAtUtc!)}';
  if (v.accessOverride != VideoAccessOverride.automatic) {
    return v.canManage ? 'Manual setting — choose Automatic to follow the class time again' : '';
  }
  if (!v.isUnlocked && v.sessionCancelled) return 'Class cancelled';
  return v.canManage ? unlockRuleLabel(v.unlockRule, v.unlockOffsetMinutes) : '';
}

String _accessLabel(VideoAccessOverride a) => switch (a) {
      VideoAccessOverride.automatic => 'Automatic (follow the class time)',
      VideoAccessOverride.unlocked => 'Unlocked now',
      VideoAccessOverride.locked => 'Locked',
    };

/// One video: lock badge and detail, Watch (or Preview for a teacher while locked),
/// and — for the class teacher — the manual lock control.
class VideoCard extends StatefulWidget {
  const VideoCard({super.key, required this.video, required this.onChanged, this.showClass = false});
  final PrerecordedVideo video;
  final Future<void> Function() onChanged;
  final bool showClass;

  @override
  State<VideoCard> createState() => _VideoCardState();
}

class _VideoCardState extends State<VideoCard> {
  bool _saving = false;

  Future<void> _setAccess(VideoAccessOverride access) async {
    if (access == widget.video.accessOverride) return;
    setState(() => _saving = true);
    try {
      await AppScope.read(context).videos.setAccess(widget.video.id, access);
      if (!mounted) return;
      showSnack(context, switch (access) {
        VideoAccessOverride.unlocked => 'Video unlocked for students.',
        VideoAccessOverride.locked => 'Video locked.',
        VideoAccessOverride.automatic => 'Video follows the class time again.',
      });
      await widget.onChanged();
    } on Object catch (e) {
      if (mounted) showSnack(context, describeError(e), error: true);
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final v = widget.video;
    final t = Theme.of(context).textTheme;
    final time = AppScope.of(context).time;
    final detail = videoLockDetail(v, time);
    final meta = [
      if (widget.showClass) '${v.sessionTitle} · ${time.date(v.sessionStartUtc)}',
      if (v.durationSeconds != null) videoLengthLabel(v.durationSeconds!),
    ].join(' · ');
    return SsCard(
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
          IconTile(v.isUnlocked ? LucideIcons.circlePlay : LucideIcons.lock, color: v.isUnlocked ? SsColors.blue : SsColors.slate),
          const SizedBox(width: SsSpace.s3),
          Expanded(
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(v.title, style: t.titleSmall),
              if (meta.isNotEmpty) Text(meta, style: t.bodySmall),
            ]),
          ),
          const SizedBox(width: SsSpace.s2),
          StatusBadge(StatusStyles.videoLock(isUnlocked: v.isUnlocked, access: v.accessOverride), dense: true),
        ]),
        if (v.description != null && v.description!.isNotEmpty) ...[
          const SizedBox(height: SsSpace.s2),
          Text(v.description!, style: t.bodyMedium),
        ],
        if (detail.isNotEmpty) ...[
          const SizedBox(height: SsSpace.s2),
          Text(detail, style: t.bodySmall?.copyWith(color: SsColors.navy)),
        ],
        if (v.isPlayable || v.canManage) ...[
          const SizedBox(height: SsSpace.s3),
          Wrap(spacing: SsSpace.s2, runSpacing: SsSpace.s2, crossAxisAlignment: WrapCrossAlignment.center, children: [
            if (v.isPlayable)
              FilledButton.icon(
                icon: const Icon(LucideIcons.play),
                label: Text(v.isUnlocked ? 'Watch' : 'Preview'),
                onPressed: () => context.push('/videos/${v.id}/play', extra: v),
              ),
            if (v.canManage)
              PopupMenuButton<VideoAccessOverride>(
                tooltip: 'Change who can watch',
                enabled: !_saving,
                initialValue: v.accessOverride,
                onSelected: _setAccess,
                itemBuilder: (_) => [
                  for (final a in VideoAccessOverride.values) CheckedPopupMenuItem(value: a, checked: a == v.accessOverride, child: Text(_accessLabel(a))),
                ],
                child: IgnorePointer(
                  child: OutlinedButton.icon(
                    onPressed: _saving ? null : () {},
                    icon: _saving
                        ? const SizedBox(width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2))
                        : Icon(v.isUnlocked ? LucideIcons.lockOpen : LucideIcons.lock),
                    label: Text('Access: ${switch (v.accessOverride) {
                      VideoAccessOverride.automatic => 'Automatic',
                      VideoAccessOverride.unlocked => 'Unlocked',
                      VideoAccessOverride.locked => 'Locked',
                    }}'),
                  ),
                ),
              ),
          ]),
        ],
      ]),
    );
  }
}

String _emptyMessage(bool teacher) => teacher
    ? 'Add a pre-recorded video from the class page in the Student Sync web app. It appears here, locked until its unlock time.'
    : 'Videos your teachers add appear here. Each one unlocks automatically before or after its live class.';

/// Pre-recorded videos of one class. A section of the class page; hidden while empty.
class SessionVideosSection extends StatelessWidget {
  const SessionVideosSection({super.key, required this.sessionId, this.onLoaded});
  final String sessionId;
  final VoidCallback? onLoaded;

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    return AsyncSection<List<PrerecordedVideo>>(
      load: () => services.videos.forSession(sessionId),
      isEmpty: (v) => v.isEmpty,
      onLoaded: onLoaded,
      builder: (context, items, reload) => Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        SectionHeader('Class videos', icon: LucideIcons.monitorPlay, count: items.length),
        for (final v in items) ...[VideoCard(video: v, onChanged: reload), const SizedBox(height: SsSpace.s3)],
      ]),
    );
  }
}

/// Every video across the classes the user can see, newest class first, with
/// "Show more" paging.
class AllVideosView extends StatelessWidget {
  const AllVideosView({super.key});

  static const _pageSize = 50;

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    final teacher = services.auth.user?.role == UserRole.teacher;
    return AsyncView<PagedResult<PrerecordedVideo>>(
      load: () => services.videos.list(pageSize: _pageSize),
      isEmpty: (p) => p.items.isEmpty,
      empty: (context, _) => EmptyState(icon: LucideIcons.monitorPlay, title: 'No pre-recorded videos yet', message: _emptyMessage(teacher)),
      builder: (context, first, reload) => _PagedVideos(first: first, reload: reload, pageSize: _pageSize),
    );
  }
}

class _PagedVideos extends StatefulWidget {
  const _PagedVideos({required this.first, required this.reload, required this.pageSize});
  final PagedResult<PrerecordedVideo> first;
  final Future<void> Function() reload;
  final int pageSize;

  @override
  State<_PagedVideos> createState() => _PagedVideosState();
}

class _PagedVideosState extends State<_PagedVideos> {
  late List<PrerecordedVideo> _items = widget.first.items;
  late PagedResult<PrerecordedVideo> _last = widget.first;
  bool _loadingMore = false;

  @override
  void didUpdateWidget(_PagedVideos old) {
    super.didUpdateWidget(old);
    // A reload starts again from the first page.
    if (old.first != widget.first) {
      _items = widget.first.items;
      _last = widget.first;
    }
  }

  Future<void> _more() async {
    setState(() => _loadingMore = true);
    try {
      final next = await AppScope.read(context).videos.list(page: _last.page + 1, pageSize: widget.pageSize);
      if (!mounted) return;
      setState(() {
        _items = [..._items, ...next.items];
        _last = next;
      });
    } on Object catch (e) {
      if (mounted) showSnack(context, describeError(e), error: true);
    } finally {
      if (mounted) setState(() => _loadingMore = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final time = AppScope.of(context).time;
    final children = <Widget>[];
    String? lastSession;
    for (final v in _items) {
      if (v.sessionId != lastSession) {
        children.add(SectionHeader('${v.sessionTitle} · ${time.date(v.sessionStartUtc)}'));
        lastSession = v.sessionId;
      }
      children.add(Padding(padding: const EdgeInsets.only(bottom: SsSpace.s3), child: VideoCard(video: v, onChanged: widget.reload)));
    }
    if (_last.hasMore) {
      children.add(Center(
        child: OutlinedButton(
          onPressed: _loadingMore ? null : _more,
          child: _loadingMore ? const SizedBox(width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2)) : const Text('Show more'),
        ),
      ));
    }
    return RefreshIndicator(onRefresh: widget.reload, child: ListView(padding: pagePadding(context), children: children));
  }
}

/// Standalone "Pre-recorded videos" screen (opened from Classes).
class VideosScreen extends StatelessWidget {
  const VideosScreen({super.key});

  @override
  Widget build(BuildContext context) => Scaffold(appBar: AppBar(title: const Text('Pre-recorded videos')), body: const AllVideosView());
}
