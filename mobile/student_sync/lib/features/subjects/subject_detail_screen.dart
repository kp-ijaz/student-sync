import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/app_services.dart';
import '../../core/models/content_models.dart';
import '../../core/models/session_models.dart';
import '../../core/models/subject_models.dart';
import '../../core/theme/status_styles.dart';
import '../../core/theme/tokens.dart';
import '../../core/time/time_format.dart';
import '../../core/widgets/async_view.dart';
import '../../core/widgets/class_cover.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/session_card.dart';
import '../../core/widgets/states.dart';
import '../sessions/join_button.dart';
import '../videos/video_widgets.dart' show videoLengthLabel, videoLockDetail;
import 'subjects_screen.dart';

/// One subject: progress, the next class, then the syllabus — every chapter in order,
/// each with its live classes and videos. The chapter being taught opens by itself.
class SubjectDetailScreen extends StatelessWidget {
  const SubjectDetailScreen({super.key, required this.subjectId});
  final String subjectId;

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    return Scaffold(
      appBar: AppBar(title: const Text('Subject')),
      body: AsyncView<SubjectSyllabus>(
        load: () => services.subjects.syllabus(subjectId),
        loadingLabel: 'Loading subject',
        builder: (context, s, reload) => _SubjectBody(s: s, reload: reload),
      ),
    );
  }
}

/// Header (cover, progress, next class) above three pill tabs: Videos, Classes, Syllabus.
/// Notes and assignments belong to each class and are on the class's page.
class _SubjectBody extends StatelessWidget {
  const _SubjectBody({required this.s, required this.reload});
  final SubjectSyllabus s;
  final Future<void> Function() reload;

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).textTheme;
    final time = AppScope.of(context).time;
    final next = s.nextSession;
    return DefaultTabController(
      length: 3,
      child: NestedScrollView(
        headerSliverBuilder: (context, _) => [
          SliverPadding(
            padding: const EdgeInsets.fromLTRB(SsSpace.s4, SsSpace.s2, SsSpace.s4, 0),
            sliver: SliverList.list(children: [
              DecoratedBox(
                decoration: BoxDecoration(borderRadius: BorderRadius.circular(SsRadius.card + 4), boxShadow: SsShadow.floating),
                child: ClassCoverImage(
                  subject: s.name,
                  title: s.courseName,
                  height: 170,
                  radius: SsRadius.card + 4,
                  child: Padding(
                    padding: const EdgeInsets.all(SsSpace.s4),
                    child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                      Row(children: [
                        TeacherStack(teachers: s.teachers, size: 36),
                        const Spacer(),
                        GlassPill(label: s.group ?? s.code),
                      ]),
                      const Spacer(),
                      Text(s.courseName.toUpperCase(),
                          style: const TextStyle(fontFamily: 'Inter', fontSize: 11, fontWeight: FontWeight.w700, letterSpacing: 1.1, color: Color(0xD9FFFFFF))),
                      Semantics(
                        header: true,
                        child: Text(s.name,
                            style: const TextStyle(fontFamily: 'PlusJakartaSans', fontSize: 26, fontWeight: FontWeight.w700, letterSpacing: -0.4, color: SsColors.white)),
                      ),
                    ]),
                  ),
                ),
              ),
              if (s.description != null && s.description!.isNotEmpty) ...[
                const SizedBox(height: SsSpace.s3),
                Text(s.description!, style: t.bodyMedium?.copyWith(color: SsColors.textSecondary)),
              ],
              const SizedBox(height: SsSpace.s3),
              Row(children: [
                Expanded(child: _Stat(icon: LucideIcons.circlePlay, value: '${s.unlockedVideoCount}/${s.videoCount}', label: 'Videos')),
                const SizedBox(width: SsSpace.s2),
                Expanded(child: _Stat(icon: LucideIcons.presentation, value: '${s.completedClassCount}/${s.classCount}', label: 'Classes')),
                const SizedBox(width: SsSpace.s2),
                Expanded(child: _Stat(icon: LucideIcons.bookOpen, value: '${s.completedChapterCount}/${s.chapterCount}', label: 'Chapters')),
              ]),
              if (next != null) ...[
                const SizedBox(height: SsSpace.s3),
                _NextClassStrip(session: next, time: time),
              ],
              const SizedBox(height: SsSpace.s2),
            ]),
          ),
          SliverPersistentHeader(
            pinned: true,
            delegate: _TabsHeader(const SsTabBar(
              tabs: [Text('Videos'), Text('Classes'), Text('Syllabus')],
              padding: EdgeInsets.fromLTRB(SsSpace.s4, SsSpace.s2, SsSpace.s4, SsSpace.s2),
            )),
          ),
        ],
        body: TabBarView(children: [
          _VideosTab(s: s, reload: reload, time: time),
          _ClassesTab(s: s, reload: reload, time: time),
          _SyllabusTab(s: s, reload: reload, time: time),
        ]),
      ),
    );
  }
}

/// Keeps the tab bar pinned on the page background while the header scrolls away.
class _TabsHeader extends SliverPersistentHeaderDelegate {
  _TabsHeader(this.bar);
  final SsTabBar bar;

  @override
  double get minExtent => bar.preferredSize.height;
  @override
  double get maxExtent => bar.preferredSize.height;

  @override
  Widget build(BuildContext context, double shrinkOffset, bool overlapsContent) => ColoredBox(color: SsColors.background, child: bar);

  @override
  bool shouldRebuild(covariant _TabsHeader oldDelegate) => false;
}

/// "Next class · Mon 9 Nov · 19:00" with Join when it can be joined.
class _NextClassStrip extends StatelessWidget {
  const _NextClassStrip({required this.session, required this.time});
  final SessionListItem session;
  final TimeFormatter time;

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).textTheme;
    return SsCard(
      onTap: () => context.push('/sessions/${session.id}'),
      accent: SsColors.orange,
      padding: const EdgeInsets.all(SsSpace.s3),
      child: Row(children: [
        const IconTile(LucideIcons.calendarClock, color: SsColors.orangeDeep, size: 40),
        const SizedBox(width: SsSpace.s3),
        Expanded(
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text('Next class', style: t.labelSmall),
            Text(time.range(session.startUtc, session.endUtc), style: t.titleSmall, maxLines: 1, overflow: TextOverflow.ellipsis),
          ]),
        ),
        if (joinableNow(session, DateTime.now().toUtc()))
          JoinSessionButton(session: session)
        else
          const Icon(LucideIcons.chevronRight, size: 18, color: SsColors.slate),
      ]),
    );
  }
}

class _VideosTab extends StatelessWidget {
  const _VideosTab({required this.s, required this.reload, required this.time});
  final SubjectSyllabus s;
  final Future<void> Function() reload;
  final TimeFormatter time;

  @override
  Widget build(BuildContext context) {
    final videos = [for (final c in s.chapters) for (final v in c.videos) (v, c.title)];
    final recordings = [for (final c in s.chapters) for (final r in c.recordings) (r, c.title)];
    // Watchable first (latest class first); then locked ones, soonest to open first.
    final open = videos.where((e) => e.$1.isUnlocked).toList()..sort((a, b) => b.$1.sessionStartUtc.compareTo(a.$1.sessionStartUtc));
    final locked = videos.where((e) => !e.$1.isUnlocked).toList()
      ..sort((a, b) => (a.$1.unlocksAtUtc ?? a.$1.sessionStartUtc).compareTo(b.$1.unlocksAtUtc ?? b.$1.sessionStartUtc));
    if (videos.isEmpty && recordings.isEmpty) {
      return const EmptyState(
        icon: LucideIcons.circlePlay,
        title: 'No videos yet',
        message: 'Videos for this subject unlock before or after each class, as your teacher sets them.',
      );
    }
    return RefreshIndicator(
      onRefresh: reload,
      child: ListView(padding: pagePadding(context).copyWith(top: SsSpace.s2), children: [
        if (open.isNotEmpty) ...[
          _TabHeading('Ready to watch', open.length),
          for (final (v, chapter) in open) _VideoRow(video: v, time: time, chapter: chapter),
        ],
        if (recordings.isNotEmpty) ...[
          _TabHeading('Class recordings', recordings.length),
          for (final (r, chapter) in recordings) _RecordingRow(recording: r, chapter: chapter),
        ],
        if (locked.isNotEmpty) ...[
          _TabHeading('Unlocks later', locked.length),
          for (final (v, chapter) in locked) _VideoRow(video: v, time: time, chapter: chapter),
        ],
      ]),
    );
  }
}

enum _When { upcoming, past }

/// Upcoming or past classes of the subject (a pill switch picks which).
class _ClassesTab extends StatefulWidget {
  const _ClassesTab({required this.s, required this.reload, required this.time});
  final SubjectSyllabus s;
  final Future<void> Function() reload;
  final TimeFormatter time;

  @override
  State<_ClassesTab> createState() => _ClassesTabState();
}

class _ClassesTabState extends State<_ClassesTab> {
  _When? _when;

  @override
  Widget build(BuildContext context) {
    final now = DateTime.now().toUtc();
    final all = [for (final c in widget.s.chapters) ...c.sessions];
    final upcoming = all.where((x) => x.endUtc.isAfter(now)).toList()..sort((a, b) => a.startUtc.compareTo(b.startUtc));
    final past = all.where((x) => !x.endUtc.isAfter(now)).toList()..sort((a, b) => b.startUtc.compareTo(a.startUtc));
    if (all.isEmpty) {
      return const EmptyState(icon: LucideIcons.presentation, title: 'No classes yet', message: 'Live classes for this subject will appear here.');
    }
    final when = _when ?? (upcoming.isNotEmpty ? _When.upcoming : _When.past);
    final items = when == _When.upcoming ? upcoming : past;
    return RefreshIndicator(
      onRefresh: widget.reload,
      child: ListView(padding: pagePadding(context).copyWith(top: SsSpace.s2), children: [
        SsSegmented<_When>(
          segments: [
            (value: _When.upcoming, label: 'Upcoming (${upcoming.length})', icon: null),
            (value: _When.past, label: 'Past classes (${past.length})', icon: null),
          ],
          selected: when,
          onChanged: (v) => setState(() => _when = v),
        ),
        const SizedBox(height: SsSpace.s3),
        if (when == _When.past && past.isNotEmpty) ...[
          Text('Open a past class for its notes, assignments and recording.', style: Theme.of(context).textTheme.bodySmall),
          const SizedBox(height: SsSpace.s2),
        ],
        if (items.isEmpty)
          InlineEmpty(icon: LucideIcons.calendarCheck, text: when == _When.upcoming ? 'No upcoming classes.' : 'No past classes yet.')
        else
          for (final x in items) _ClassRow(session: x, time: widget.time),
      ]),
    );
  }
}

class _SyllabusTab extends StatelessWidget {
  const _SyllabusTab({required this.s, required this.reload, required this.time});
  final SubjectSyllabus s;
  final Future<void> Function() reload;
  final TimeFormatter time;

  /// The chapter to open first: the one in progress, else the next one coming up.
  int? get _focus {
    final i = s.chapters.indexWhere((c) => c.status == ChapterStatus.inProgress);
    if (i >= 0) return i;
    final j = s.chapters.indexWhere((c) => c.status == ChapterStatus.upcoming);
    return j >= 0 ? j : null;
  }

  @override
  Widget build(BuildContext context) {
    if (s.chapters.isEmpty) {
      return const EmptyState(icon: LucideIcons.listChecks, title: 'Syllabus not added yet', message: 'Your institute will add the chapters for this subject.');
    }
    final focus = _focus;
    return RefreshIndicator(
      onRefresh: reload,
      child: ListView(padding: pagePadding(context).copyWith(top: SsSpace.s2), children: [
        SsCard(child: SyllabusProgress(done: s.completedChapterCount, total: s.chapterCount, value: s.progress)),
        const SizedBox(height: SsSpace.s3),
        for (var i = 0; i < s.chapters.length; i++) ...[
          _ChapterTile(chapter: s.chapters[i], number: s.chapters[i].chapterId == null ? null : i + 1, initiallyOpen: i == focus, time: time),
          const SizedBox(height: SsSpace.s3),
        ],
      ]),
    );
  }
}

class _TabHeading extends StatelessWidget {
  const _TabHeading(this.title, this.count);
  final String title;
  final int count;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(top: SsSpace.s3, bottom: SsSpace.s2),
        child: Row(children: [
          Semantics(header: true, child: Text(title, style: Theme.of(context).textTheme.titleSmall)),
          const SizedBox(width: SsSpace.s2),
          Text('$count', style: Theme.of(context).textTheme.labelSmall),
        ]),
      );
}

class _Stat extends StatelessWidget {
  const _Stat({required this.icon, required this.value, required this.label});
  final IconData icon;
  final String value;
  final String label;

  @override
  Widget build(BuildContext context) => SsCard(
        padding: const EdgeInsets.symmetric(horizontal: SsSpace.s3, vertical: SsSpace.s3),
        semanticLabel: '$label: $value',
        child: ExcludeSemantics(
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Icon(icon, size: 18, color: SsColors.blue),
            const SizedBox(height: SsSpace.s2),
            Text(value, style: Theme.of(context).textTheme.titleLarge?.copyWith(fontFeatures: const [FontFeature.tabularFigures()])),
            Text(label, style: Theme.of(context).textTheme.labelSmall),
          ]),
        ),
      );
}

/// One chapter of the syllabus: number/status marker, title and counts; expands to its
/// live classes and videos.
class _ChapterTile extends StatelessWidget {
  const _ChapterTile({required this.chapter, required this.number, required this.initiallyOpen, required this.time});
  final SyllabusChapter chapter;
  final int? number;
  final bool initiallyOpen;
  final TimeFormatter time;

  static (String, Color) _status(ChapterStatus s) => switch (s) {
        ChapterStatus.completed => ('Completed', SsColors.success),
        ChapterStatus.inProgress => ('In progress', SsColors.blue),
        ChapterStatus.upcoming => ('Coming up', SsColors.orangeDeep),
        ChapterStatus.notScheduled => ('Not scheduled yet', SsColors.slate),
      };

  @override
  Widget build(BuildContext context) {
    final c = chapter;
    final t = Theme.of(context).textTheme;
    final (label, color) = _status(c.status);
    final counts = [
      '${c.sessions.length} ${c.sessions.length == 1 ? 'class' : 'classes'}',
      if (c.watchableCount > 0) '${c.watchableCount} ${c.watchableCount == 1 ? 'video' : 'videos'}',
    ].join(' · ');
    final empty = c.sessions.isEmpty && c.watchableCount == 0;
    return SsCard(
      padding: EdgeInsets.zero,
      accent: c.status == ChapterStatus.inProgress ? SsColors.blue : null,
      child: Theme(
        data: Theme.of(context).copyWith(dividerColor: Colors.transparent),
        child: ExpansionTile(
          initiallyExpanded: initiallyOpen && !empty,
          enabled: !empty,
          tilePadding: const EdgeInsets.symmetric(horizontal: SsSpace.s4, vertical: SsSpace.s1),
          childrenPadding: const EdgeInsets.fromLTRB(SsSpace.s4, 0, SsSpace.s4, SsSpace.s4),
          leading: _Marker(number: number, status: c.status),
          title: Text(c.title, style: t.titleSmall),
          subtitle: Padding(
            padding: const EdgeInsets.only(top: 2),
            child: Row(children: [
              Container(width: 7, height: 7, decoration: BoxDecoration(color: color, shape: BoxShape.circle)),
              const SizedBox(width: 6),
              Flexible(child: Text(empty ? label : '$label · $counts', style: t.labelSmall, maxLines: 1, overflow: TextOverflow.ellipsis)),
            ]),
          ),
          trailing: empty ? const SizedBox.shrink() : null,
          expandedCrossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (c.sessions.isNotEmpty) ...[
              Text('Live classes', style: t.labelMedium?.copyWith(fontWeight: FontWeight.w700, color: SsColors.textPrimary)),
              const SizedBox(height: SsSpace.s2),
              for (final s in c.sessions) _ClassRow(session: s, time: time),
            ],
            if (c.watchableCount > 0) ...[
              const SizedBox(height: SsSpace.s3),
              Text('Videos', style: t.labelMedium?.copyWith(fontWeight: FontWeight.w700, color: SsColors.textPrimary)),
              const SizedBox(height: SsSpace.s2),
              for (final v in c.videos) _VideoRow(video: v, time: time),
              for (final r in c.recordings) _RecordingRow(recording: r),
            ],
          ],
        ),
      ),
    );
  }
}

class _Marker extends StatelessWidget {
  const _Marker({required this.number, required this.status});
  final int? number;
  final ChapterStatus status;

  @override
  Widget build(BuildContext context) {
    final done = status == ChapterStatus.completed;
    final active = status == ChapterStatus.inProgress;
    return ExcludeSemantics(
      child: Container(
        width: 38,
        height: 38,
        decoration: BoxDecoration(
          shape: BoxShape.circle,
          color: done ? SsColors.success : (active ? SsColors.blue : SsColors.fill),
          border: status == ChapterStatus.upcoming ? Border.all(color: SsColors.blue, width: 2) : null,
        ),
        alignment: Alignment.center,
        child: done
            ? const Icon(LucideIcons.check, size: 18, color: SsColors.white)
            : number == null
                ? Icon(LucideIcons.layers, size: 16, color: active ? SsColors.white : SsColors.slate)
                : Text('$number',
                    style: TextStyle(
                      fontFamily: 'PlusJakartaSans',
                      fontSize: 15,
                      fontWeight: FontWeight.w700,
                      color: active ? SsColors.white : (status == ChapterStatus.upcoming ? SsColors.blue : SsColors.slate),
                    )),
      ),
    );
  }
}

/// A live class inside a chapter: date, title, time and status; Join when it can be joined.
class _ClassRow extends StatelessWidget {
  const _ClassRow({required this.session, required this.time});
  final SessionListItem session;
  final TimeFormatter time;

  @override
  Widget build(BuildContext context) {
    final s = session;
    final t = Theme.of(context).textTheme;
    final status = StatusStyles.session(s.status);
    final local = time.inViewerZone(s.startUtc);
    return Padding(
      padding: const EdgeInsets.only(bottom: SsSpace.s2),
      child: Material(
        color: SsColors.background,
        borderRadius: BorderRadius.circular(SsRadius.control),
        child: InkWell(
          borderRadius: BorderRadius.circular(SsRadius.control),
          onTap: () => context.push('/sessions/${s.id}'),
          child: Padding(
            padding: const EdgeInsets.all(SsSpace.s2),
            child: Row(children: [
              Container(
                width: 46,
                padding: const EdgeInsets.symmetric(vertical: 6),
                decoration: BoxDecoration(color: SsColors.white, borderRadius: BorderRadius.circular(10)),
                child: Column(children: [
                  Text(DateFormat('MMM').format(local).toUpperCase(),
                      style: const TextStyle(fontFamily: 'Inter', fontSize: 10, fontWeight: FontWeight.w700, color: SsColors.blue)),
                  Text(DateFormat('d').format(local),
                      style: const TextStyle(fontFamily: 'PlusJakartaSans', fontSize: 17, fontWeight: FontWeight.w700, color: SsColors.navy)),
                ]),
              ),
              const SizedBox(width: SsSpace.s3),
              Expanded(
                child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  Text(s.title, style: t.titleSmall?.copyWith(decoration: status.strikeThrough ? TextDecoration.lineThrough : null), maxLines: 1, overflow: TextOverflow.ellipsis),
                  Text('${time.time(s.startUtc)} · ${_shortName(s.teacherName)}', style: t.labelSmall, maxLines: 1, overflow: TextOverflow.ellipsis),
                ]),
              ),
              const SizedBox(width: SsSpace.s2),
              joinableNow(s, DateTime.now().toUtc()) ? JoinSessionButton(session: s) : StatusBadge(status, dense: true),
            ]),
          ),
        ),
      ),
    );
  }
}

String _shortName(String name) => name.replaceAll(RegExp(r'\s*\([^)]*\)'), '').trim();

/// A pre-recorded video inside a chapter: Watch when unlocked, otherwise when it opens.
class _VideoRow extends StatelessWidget {
  const _VideoRow({required this.video, required this.time, this.chapter});
  final PrerecordedVideo video;
  final TimeFormatter time;

  /// Shown on the Videos tab, where videos from every chapter are listed together.
  final String? chapter;

  @override
  Widget build(BuildContext context) {
    final v = video;
    final t = Theme.of(context).textTheme;
    final detail = v.isUnlocked ? (v.durationSeconds == null ? 'Pre-recorded video' : videoLengthLabel(v.durationSeconds!)) : videoLockDetail(v, time);
    return _MediaRow(
      icon: v.isUnlocked ? LucideIcons.circlePlay : LucideIcons.lock,
      color: v.isUnlocked ? SsColors.blue : SsColors.slate,
      title: v.title,
      detail: [?chapter, detail.isEmpty ? 'Locked until around the class' : detail].join(' · '),
      trailing: v.isPlayable
          ? FilledButton.tonal(onPressed: () => context.push('/videos/${v.id}/play', extra: v), child: Text(v.isUnlocked ? 'Watch' : 'Preview'))
          : Text('Locked', style: t.labelSmall),
    );
  }
}

class _RecordingRow extends StatelessWidget {
  const _RecordingRow({required this.recording, this.chapter});
  final Recording recording;
  final String? chapter;

  @override
  Widget build(BuildContext context) {
    final r = recording;
    return _MediaRow(
      icon: LucideIcons.clapperboard,
      color: SsColors.navy,
      title: r.title,
      detail: [?chapter, 'Class recording', if (r.durationSeconds != null) formatDuration(Duration(seconds: r.durationSeconds!)), if (r.completed) 'Watched'].join(' · '),
      trailing: r.isPlayable ? FilledButton.tonal(onPressed: () => context.push('/recordings/${r.id}/play', extra: r), child: const Text('Watch')) : null,
    );
  }
}

class _MediaRow extends StatelessWidget {
  const _MediaRow({required this.icon, required this.color, required this.title, required this.detail, this.trailing});
  final IconData icon;
  final Color color;
  final String title;
  final String detail;
  final Widget? trailing;

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).textTheme;
    return Padding(
      padding: const EdgeInsets.only(bottom: SsSpace.s2),
      child: Container(
        padding: const EdgeInsets.all(SsSpace.s2),
        decoration: BoxDecoration(color: SsColors.background, borderRadius: BorderRadius.circular(SsRadius.control)),
        child: Row(children: [
          IconTile(icon, color: color, size: 40, background: SsColors.white),
          const SizedBox(width: SsSpace.s3),
          Expanded(
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(title, style: t.titleSmall, maxLines: 2, overflow: TextOverflow.ellipsis),
              Text(detail, style: t.labelSmall, maxLines: 1, overflow: TextOverflow.ellipsis),
            ]),
          ),
          if (trailing != null) ...[const SizedBox(width: SsSpace.s2), trailing!],
        ]),
      ),
    );
  }
}
