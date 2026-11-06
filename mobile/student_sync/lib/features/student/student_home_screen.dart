import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/app_services.dart';
import '../../core/models/dashboard_models.dart';
import '../../core/models/session_models.dart';
import '../../core/theme/tokens.dart';
import '../../core/widgets/async_view.dart';
import '../../core/widgets/avatar.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/home_greeting.dart';
import '../../core/widgets/notification_bell.dart';
import '../../core/widgets/session_card.dart';
import '../doubts/doubt_card.dart';
import '../learning/assignment_card.dart';
import '../sessions/join_button.dart';
import '../sessions/timetable_screen.dart' show TimetableSlot;

class StudentHomeScreen extends StatelessWidget {
  const StudentHomeScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    return Scaffold(
      appBar: AppBar(
        toolbarHeight: 76,
        title: const HomeGreeting(),
        actions: const [NotificationBell(), SizedBox(width: SsSpace.s4)],
      ),
      body: AsyncView<StudentDashboard>(
        load: () async {
          final d = await services.dashboards.student();
          await services.unread.refresh();
          return d;
        },
        builder: (context, d, reload) => RefreshIndicator(
          onRefresh: reload,
          child: _StudentHomeBody(d: d),
        ),
      ),
    );
  }
}

/// A teacher the student has classes with, gathered from their upcoming classes.
class _Teacher {
  _Teacher(this.id, this.name, this.photoUrl, this.subject);
  final String id;
  final String name;
  final String? photoUrl;
  final String? subject;
  final classes = <SessionListItem>[];
}

class _StudentHomeBody extends StatelessWidget {
  const _StudentHomeBody({required this.d});
  final StudentDashboard d;

  List<_Teacher> _teachers() {
    final byId = <String, _Teacher>{};
    for (final s in [?d.nextSession, ...d.upcomingSessions]) {
      final t = byId.putIfAbsent(s.teacherId, () => _Teacher(s.teacherId, s.teacherName, s.teacherPhotoUrl, s.subjectName));
      if (t.classes.every((c) => c.id != s.id)) t.classes.add(s);
    }
    return byId.values.toList();
  }

  void _showTeacher(BuildContext context, _Teacher teacher) {
    final time = AppScope.read(context).time;
    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      builder: (sheet) => SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(SsSpace.s4, 0, SsSpace.s4, SsSpace.s4),
          child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
            Row(children: [
              UserAvatar(name: teacher.name, photoUrl: teacher.photoUrl, size: 64),
              const SizedBox(width: SsSpace.s4),
              Expanded(
                child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  Text(teacher.name, style: Theme.of(sheet).textTheme.titleLarge),
                  if (teacher.subject != null) Text(teacher.subject!, style: Theme.of(sheet).textTheme.bodySmall),
                ]),
              ),
            ]),
            SectionHeader('Upcoming classes', count: teacher.classes.length),
            ConstrainedBox(
              constraints: BoxConstraints(maxHeight: MediaQuery.sizeOf(sheet).height * 0.5),
              child: ListView(shrinkWrap: true, children: [
                for (final s in teacher.classes) ...[
                  SessionCard(
                    session: s,
                    time: time,
                    showTeacher: false,
                    onTap: () {
                      Navigator.pop(sheet);
                      context.push('/sessions/${s.id}');
                    },
                  ),
                  const SizedBox(height: SsSpace.s3),
                ],
              ]),
            ),
          ]),
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    final time = services.time;
    final next = d.nextSession;
    final now = DateTime.now().toUtc();
    final today = time.dayKey(now);
    // Today's remaining classes, hour by hour; "Coming up" then shows the days after.
    final todays = [?next, ...d.upcomingSessions.where((s) => s.id != next?.id)]
        .where((s) => time.dayKey(s.startUtc) == today && s.endUtc.isAfter(now))
        .toList()
      ..sort((a, b) => a.startUtc.compareTo(b.startUtc));
    final later = d.upcomingSessions.where((s) => s.id != next?.id && time.dayKey(s.startUtc) != today).toList();
    final teachers = _teachers();

    return ListView(padding: pagePadding(context), children: [
      const SizedBox(height: SsSpace.s2),
      NextClassHero(
        session: next,
        time: time,
        onTap: next == null ? null : () => context.push('/sessions/${next.id}'),
        action: next != null && canJoin(next.status, next.hasMeetingLink) ? JoinSessionButton(session: next, expand: true) : null,
      ),
      const SizedBox(height: SsSpace.s4),
      ShortcutGrid(tiles: [
        ShortcutTile(icon: LucideIcons.calendarDays, title: 'Timetable', onTap: () => context.push('/schedule')),
        ShortcutTile(icon: LucideIcons.monitorPlay, title: 'Videos', color: SsColors.navy, onTap: () => context.push('/videos')),
        ShortcutTile(icon: LucideIcons.clipboardList, title: 'Homework', color: SsColors.success, onTap: () => context.go('/student/learning')),
        ShortcutTile(icon: LucideIcons.chartLine, title: 'My progress', color: SsColors.orangeDeep, onTap: () => context.push('/progress')),
      ]),
      if (teachers.isNotEmpty) ...[
        const SectionHeader('Your teachers'),
        GridView.count(
          crossAxisCount: 2,
          shrinkWrap: true,
          physics: const NeverScrollableScrollPhysics(),
          padding: EdgeInsets.zero,
          mainAxisSpacing: SsSpace.s3,
          crossAxisSpacing: SsSpace.s3,
          childAspectRatio: 0.92,
          children: [
            for (final teacher in teachers.take(4))
              TeacherPhotoCard(name: teacher.name, photoUrl: teacher.photoUrl, subtitle: teacher.subject, onTap: () => _showTeacher(context, teacher)),
          ],
        ),
      ],
      SectionHeader("Today's timetable",
          icon: LucideIcons.clock, count: todays.length, trailing: SeeAllButton(label: 'Full timetable', onPressed: () => context.push('/schedule'))),
      if (todays.isEmpty)
        const InlineEmpty(icon: LucideIcons.calendarCheck, text: 'No more classes today.')
      else
        for (final s in todays) TimetableSlot(session: s, time: time),
      if (later.isNotEmpty) ...[
        SectionHeader('Coming up', trailing: SeeAllButton(label: 'Timetable', onPressed: () => context.push('/schedule'))),
        SizedBox(
          height: 206,
          child: ListView.separated(
            scrollDirection: Axis.horizontal,
            clipBehavior: Clip.none,
            itemCount: later.length.clamp(0, 8),
            separatorBuilder: (_, _) => const SizedBox(width: SsSpace.s3),
            itemBuilder: (context, i) => ClassCarouselCard(session: later[i], time: time, onTap: () => context.push('/sessions/${later[i].id}')),
          ),
        ),
      ],
      SectionHeader('Due assignments', trailing: SeeAllButton(label: 'Learning', onPressed: () => context.go('/student/learning'))),
      if (d.dueAssignments.isEmpty)
        const InlineEmpty(icon: LucideIcons.partyPopper, text: "You're all caught up — no assignments due.")
      else
        for (final a in d.dueAssignments) ...[
          AssignmentCard(assignment: a),
          const SizedBox(height: SsSpace.s3),
        ],
      SectionHeader('Open doubts', trailing: SeeAllButton(label: 'Doubts', onPressed: () => context.go('/student/doubts'))),
      if (d.openDoubts.isEmpty)
        const InlineEmpty(icon: LucideIcons.messageCircleQuestionMark, text: 'No open doubts. Ask one any time from the Doubts tab.')
      else
        for (final x in d.openDoubts) ...[
          DoubtCard(doubt: x, showStudent: false),
          const SizedBox(height: SsSpace.s3),
        ],
      if (d.recentContent.isNotEmpty) ...[
        const SectionHeader('New in your classes'),
        SsListGroup(children: [
          for (final c in d.recentContent)
            SsListRow(
              icon: c.kind == 'Recording' ? LucideIcons.clapperboard : c.kind == 'Material' ? LucideIcons.folder : LucideIcons.notebookText,
              color: c.kind == 'Recording' ? SsColors.navy : c.kind == 'Material' ? SsColors.success : SsColors.blue,
              title: c.title,
              subtitle: '${c.kind} · ${c.sessionTitle} · ${time.date(c.atUtc)}',
              onTap: () => context.push('/sessions/${c.sessionId}?tab=${_tabFor(c.kind)}'),
            ),
        ]),
      ],
    ]);
  }

  static String _tabFor(String kind) => switch (kind) {
        'Recording' => 'recordings',
        'Material' => 'materials',
        _ => 'notes',
      };
}
