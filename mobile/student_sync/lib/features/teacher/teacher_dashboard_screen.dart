import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/app_services.dart';
import '../../core/models/dashboard_models.dart';
import '../../core/theme/tokens.dart';
import '../../core/widgets/async_view.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/home_greeting.dart';
import '../../core/widgets/notification_bell.dart';
import '../../core/widgets/session_card.dart';
import '../doubts/doubt_card.dart';
import '../sessions/join_button.dart';
import 'tasks_screen.dart';

class TeacherDashboardScreen extends StatelessWidget {
  const TeacherDashboardScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    return Scaffold(
      appBar: AppBar(
        toolbarHeight: 76,
        title: const HomeGreeting(),
        actions: const [NotificationBell(), SizedBox(width: SsSpace.s4)],
      ),
      body: AsyncView<TeacherDashboard>(
        load: () async {
          final d = await services.dashboards.teacher();
          await services.unread.refresh();
          return d;
        },
        builder: (context, d, reload) => RefreshIndicator(onRefresh: reload, child: _Body(d: d, reload: reload)),
      ),
    );
  }
}

class _Body extends StatelessWidget {
  const _Body({required this.d, required this.reload});
  final TeacherDashboard d;
  final Future<void> Function() reload;

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    final time = services.time;
    final t = Theme.of(context).textTheme;
    final next = d.nextSession;

    Future<void> openAttendance(String sessionId) async {
      await context.push('/sessions/$sessionId?tab=attendance');
      await reload();
    }

    return ListView(padding: pagePadding(context), children: [
      const SizedBox(height: SsSpace.s2),
      NextClassHero(
        session: next,
        time: time,
        showTeacher: false,
        onTap: next == null ? null : () => context.push('/sessions/${next.id}'),
        action: next != null && canJoin(next.status, next.hasMeetingLink) ? JoinSessionButton(session: next, expand: true) : null,
      ),
      if (d.stats.isNotEmpty) ...[
        const SizedBox(height: SsSpace.s4),
        StatGrid(children: [for (final s in d.stats) StatCard(label: s.label, value: s.value, hint: s.hint)]),
      ],
      const SizedBox(height: SsSpace.s4),
      ShortcutGrid(tiles: [
        ShortcutTile(icon: LucideIcons.calendarDays, title: 'Timetable', onTap: () => context.go('/teacher/classes')),
        ShortcutTile(icon: LucideIcons.monitorPlay, title: 'Videos', color: SsColors.navy, onTap: () => context.push('/videos')),
        ShortcutTile(icon: LucideIcons.award, title: 'Grading', color: SsColors.success, onTap: () => context.go('/teacher/tasks')),
        ShortcutTile(icon: LucideIcons.messageCircleQuestionMark, title: 'Doubts', color: SsColors.orangeDeep, onTap: () => context.go('/teacher/tasks?tab=doubts')),
      ]),
      if (d.todaySessions.isNotEmpty) ...[
        SectionHeader('Today', count: d.todaySessions.length),
        SizedBox(
          height: 206,
          child: ListView.separated(
            scrollDirection: Axis.horizontal,
            clipBehavior: Clip.none,
            itemCount: d.todaySessions.length,
            separatorBuilder: (_, _) => const SizedBox(width: SsSpace.s3),
            itemBuilder: (context, i) => ClassCarouselCard(
              session: d.todaySessions[i],
              time: time,
              showTeacher: false,
              onTap: () => context.push('/sessions/${d.todaySessions[i].id}'),
            ),
          ),
        ),
      ],
      if (d.pendingAttendance.isNotEmpty) ...[
        SectionHeader('Attendance to mark (${d.pendingAttendance.length})'),
        for (final s in d.pendingAttendance) ...[
          SessionCard(
            session: s,
            time: time,
            showTeacher: false,
            onTap: () => openAttendance(s.id),
            action: SizedBox(
              width: double.infinity,
              child: FilledButton.tonalIcon(
                onPressed: () => openAttendance(s.id),
                icon: const Icon(LucideIcons.clipboardCheck, size: 18),
                label: const Text('Mark attendance'),
              ),
            ),
          ),
          const SizedBox(height: SsSpace.s3),
        ],
      ],
      SectionHeader('Submissions to grade (${d.submissionsToGrade.length})',
          trailing: d.submissionsToGrade.isEmpty ? null : SeeAllButton(label: 'Tasks', onPressed: () => context.go('/teacher/tasks'))),
      if (d.submissionsToGrade.isEmpty)
        const InlineEmpty(icon: LucideIcons.circleCheckBig, text: 'Nothing waiting to be graded.')
      else
        for (final s in d.submissionsToGrade.take(3)) ...[SubmissionToGradeCard(item: s, onReturn: reload), const SizedBox(height: SsSpace.s3)],
      SectionHeader('Doubts to answer (${d.unresolvedDoubts.length})',
          trailing: d.unresolvedDoubts.isEmpty ? null : SeeAllButton(label: 'Doubts', onPressed: () => context.go('/teacher/tasks?tab=doubts'))),
      if (d.unresolvedDoubts.isEmpty)
        const InlineEmpty(icon: LucideIcons.messageCircleQuestionMark, text: 'No open doubts.')
      else
        for (final x in d.unresolvedDoubts.take(3)) ...[DoubtCard(doubt: x, onReturn: reload), const SizedBox(height: SsSpace.s3)],
      if (d.pendingAiApprovals.isNotEmpty) ...[
        SectionHeader('AI drafts awaiting your review (${d.pendingAiApprovals.length})'),
        SsCard(
          accent: SsColors.orange,
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            for (final a in d.pendingAiApprovals)
              Padding(
                padding: const EdgeInsets.only(bottom: SsSpace.s3),
                child: Row(children: [
                  const IconTile(LucideIcons.sparkles, color: SsColors.orangeText, background: SsColors.orange50, size: 36),
                  const SizedBox(width: SsSpace.s3),
                  Expanded(
                    child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                      Text(a.sessionTitle, style: t.titleSmall),
                      Text('Drafted ${time.dateTime(a.createdAtUtc)}', style: t.bodySmall),
                    ]),
                  ),
                ]),
              ),
            Text('AI drafts are not visible to students until you approve them. Review and approve drafts in the web app.', style: t.bodySmall),
          ]),
        ),
      ],
      if (d.upcomingSessions.length > 1) ...[
        SectionHeader('Coming up', trailing: SeeAllButton(label: 'All classes', onPressed: () => context.go('/teacher/classes'))),
        for (final s in d.upcomingSessions.where((s) => s.id != next?.id).take(3)) ...[
          SessionCard(session: s, time: time, showTeacher: false, onTap: () => context.push('/sessions/${s.id}')),
          const SizedBox(height: SsSpace.s3),
        ],
      ],
    ]);
  }
}
