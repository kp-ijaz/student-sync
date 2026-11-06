import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/app_services.dart';
import '../../core/models/common.dart';
import '../../core/models/enums.dart';
import '../../core/models/learning_models.dart';
import '../../core/theme/status_styles.dart';
import '../../core/theme/tokens.dart';
import '../../core/time/time_format.dart';
import '../../core/widgets/async_view.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/illustration.dart';
import '../../core/widgets/notification_bell.dart';
import '../../core/widgets/states.dart';
import '../videos/video_widgets.dart';
import 'assignment_card.dart';

/// Student "Learning": assignments, exams and pre-recorded class videos, one tab each.
class LearningScreen extends StatelessWidget {
  const LearningScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    return DefaultTabController(
      length: 3,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('Learning'),
          actions: tabBarActions,
          bottom: const SsTabBar(tabs: [Text('Assignments'), Text('Exams'), Text('Videos')]),
        ),
        body: TabBarView(children: [
          AsyncView<PagedResult<AssignmentListItem>>(
            load: () => services.learning.assignments(),
            isEmpty: (p) => p.items.isEmpty,
            empty: (context, _) => const EmptyState(
              icon: LucideIcons.clipboardList,
              illustration: Illustration.allDone,
              title: 'No assignments',
              message: 'Assignments from your teachers will appear here.',
            ),
            builder: (context, page, reload) {
              final items = [...page.items]..sort(_assignmentOrder);
              return RefreshIndicator(
                onRefresh: reload,
                child: ListView.separated(
                  padding: pagePadding(context).copyWith(top: SsSpace.s2),
                  itemCount: items.length,
                  separatorBuilder: (_, _) => const SizedBox(height: SsSpace.s3),
                  itemBuilder: (context, i) => AssignmentCard(assignment: items[i]),
                ),
              );
            },
          ),
          AsyncView<List<ExamListItem>>(
            load: services.learning.exams,
            isEmpty: (e) => e.isEmpty,
            empty: (context, _) => const EmptyState(icon: LucideIcons.fileQuestionMark, title: 'No exams', message: 'Exams assigned to you will appear here.'),
            builder: (context, exams, reload) => RefreshIndicator(
              onRefresh: reload,
              child: ListView.separated(
                padding: pagePadding(context).copyWith(top: SsSpace.s2),
                itemCount: exams.length,
                separatorBuilder: (_, _) => const SizedBox(height: SsSpace.s3),
                itemBuilder: (context, i) => ExamCard(exam: exams[i], onReturn: reload),
              ),
            ),
          ),
          const AllVideosView(),
        ]),
      ),
    );
  }

  /// Work still to do first (by due date), then submitted/graded.
  static int _assignmentOrder(AssignmentListItem a, AssignmentListItem b) {
    int rank(AssignmentListItem x) => switch (x.mySubmissionStatus) {
          null || SubmissionStatus.draft || SubmissionStatus.returnedForCorrection => 0,
          _ => 1,
        };
    final r = rank(a).compareTo(rank(b));
    return r != 0 ? r : a.dueAtUtc.compareTo(b.dueAtUtc);
  }
}

class ExamCard extends StatelessWidget {
  const ExamCard({super.key, required this.exam, required this.onReturn});
  final ExamListItem exam;
  final Future<void> Function() onReturn;

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).textTheme;
    final time = AppScope.of(context).time;
    final e = exam;
    final now = DateTime.now().toUtc();
    final open = e.isOpenAt(now);
    final canStart = open && (e.hasInProgressAttempt || e.hasAttemptsLeft);

    final String availability;
    if (now.isBefore(e.opensAtUtc)) {
      availability = 'Opens ${time.dateTime(e.opensAtUtc)} (${time.viewerZoneId})';
    } else if (open) {
      availability = 'Open until ${time.dateTime(e.closesAtUtc)}';
    } else {
      availability = 'Closed ${time.dateTime(e.closesAtUtc)}';
    }

    return SsCard(
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
          const IconTile(LucideIcons.fileQuestionMark, color: SsColors.navy),
          const SizedBox(width: SsSpace.s3),
          Expanded(child: Text(e.title, style: t.titleSmall)),
          const SizedBox(width: SsSpace.s2),
          if (e.hasInProgressAttempt)
            const StatusBadge(StatusStyle('In progress', SsColors.orange), dense: true)
          else
            StatusBadge(StatusStyles.exam(e.status), dense: true),
        ]),
        const SizedBox(height: SsSpace.s1),
        Text('${e.subjectName} · ${e.questionCount} questions · ${formatMarks(e.totalMarks)} marks · ${e.durationMinutes} min', style: t.bodySmall),
        Text(availability, style: t.bodySmall),
        Text('Attempts: ${e.attemptsUsed} of ${e.maxAttempts}', style: t.labelSmall),
        if (e.resultsReleased && e.myBestScore != null)
          Padding(
            padding: const EdgeInsets.only(top: SsSpace.s1),
            child: Text('Best score: ${formatMarks(e.myBestScore!)}/${formatMarks(e.totalMarks)}', style: t.titleSmall?.copyWith(color: SsColors.success)),
          )
        else if (e.attemptsUsed > 0 && !e.resultsReleased)
          Padding(
            padding: const EdgeInsets.only(top: SsSpace.s1),
            child: Text('Results have not been released yet.', style: t.labelSmall),
          ),
        if (canStart) ...[
          const SizedBox(height: SsSpace.s3),
          SizedBox(
            width: double.infinity,
            child: FilledButton.icon(
              icon: Icon(e.hasInProgressAttempt ? LucideIcons.play : LucideIcons.timer),
              label: Text(e.hasInProgressAttempt ? 'Resume exam' : 'Start exam'),
              onPressed: () async {
                final confirmed = e.hasInProgressAttempt || await _confirmStart(context);
                if (!confirmed || !context.mounted) return;
                await context.push('/exams/${e.id}/attempt');
                await onReturn();
              },
            ),
          ),
        ],
        if (e.canViewResult) ...[
          SizedBox(height: canStart ? SsSpace.s2 : SsSpace.s3),
          SizedBox(
            width: double.infinity,
            child: OutlinedButton.icon(
              icon: const Icon(LucideIcons.clipboardCheck),
              label: const Text('View result'),
              onPressed: () async {
                await context.push('/attempts/${e.latestAttemptId}/result');
                await onReturn();
              },
            ),
          ),
        ],
      ]),
    );
  }

  Future<bool> _confirmStart(BuildContext context) async {
    final result = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text('Start "${exam.title}"?'),
        content: Text(
          'You will have ${exam.durationMinutes} minutes. The timer keeps running if you leave the app, '
          'and your answers are submitted automatically when time is up. '
          'This uses attempt ${exam.attemptsUsed + 1} of ${exam.maxAttempts}.',
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Not now')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Start exam')),
        ],
      ),
    );
    return result ?? false;
  }
}
