import 'package:flutter/material.dart';

import '../../core/app_services.dart';
import '../../core/models/learning_models.dart';
import '../../core/theme/status_styles.dart';
import '../../core/theme/tokens.dart';
import '../../core/time/time_format.dart';
import '../../core/widgets/async_view.dart';
import '../../core/widgets/common.dart';

class ExamResultScreen extends StatelessWidget {
  const ExamResultScreen({super.key, required this.attemptId, this.initial});
  final String attemptId;

  /// Result returned by submit; avoids a second request right after submitting.
  final AttemptResult? initial;

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    var first = initial;
    return Scaffold(
      appBar: AppBar(title: const Text('Exam result')),
      body: AsyncView<AttemptResult>(
        load: () async {
          final cached = first;
          first = null;
          return cached ?? services.learning.result(attemptId);
        },
        builder: (context, r, reload) => RefreshIndicator(onRefresh: reload, child: _ResultBody(result: r)),
      ),
    );
  }
}

class _ResultBody extends StatelessWidget {
  const _ResultBody({required this.result});
  final AttemptResult result;

  @override
  Widget build(BuildContext context) {
    final r = result;
    final t = Theme.of(context).textTheme;
    final time = AppScope.of(context).time;
    return ListView(padding: pagePadding(context), children: [
      const SizedBox(height: SsSpace.s2),
      Semantics(header: true, child: Text(r.examTitle, style: t.headlineSmall)),
      const SizedBox(height: SsSpace.s2),
      Wrap(spacing: SsSpace.s2, children: [StatusBadge(StatusStyles.attempt(r.status))]),
      if (r.submittedAtUtc != null) ...[
        const SizedBox(height: SsSpace.s1),
        Text('Submitted ${time.dateTime(r.submittedAtUtc!)}', style: t.bodySmall),
      ],
      const SizedBox(height: SsSpace.s4),
      SsCard(
        color: SsColors.blue50,
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          if (r.resultsReleased && r.score != null) ...[
            Text('Your score', style: t.bodySmall),
            Text('${formatMarks(r.score!)} / ${formatMarks(r.totalMarks)}', style: t.displaySmall),
            if (r.requiresManualGrading) Text('Some answers are still being marked by your teacher.', style: t.bodySmall),
          ] else ...[
            Text('Your answers have been submitted.', style: t.titleMedium),
            const SizedBox(height: SsSpace.s1),
            Text(
              r.requiresManualGrading
                  ? 'Your teacher will mark some answers by hand. Results appear here once they are released.'
                  : 'Results appear here once your teacher releases them.',
              style: t.bodyMedium,
            ),
          ],
        ]),
      ),
      if (r.answers.isNotEmpty) ...[
        const SectionHeader('Answers'),
        for (final (i, a) in r.answers.indexed) ...[
          SsCard(
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Expanded(child: Text('${i + 1}. ${a.stem}', style: t.titleSmall)),
                const SizedBox(width: SsSpace.s2),
                StatusBadge(
                  a.isCorrect == null
                      ? const StatusStyle('Marked by teacher', SsColors.warning)
                      : a.isCorrect!
                          ? const StatusStyle('Correct', SsColors.success)
                          : const StatusStyle('Incorrect', SsColors.error),
                  dense: true,
                ),
              ]),
              const SizedBox(height: SsSpace.s1),
              Text('${a.awardedMarks == null ? '–' : formatMarks(a.awardedMarks!)} / ${formatMarks(a.marks)} marks', style: t.bodySmall),
              if (a.correctAnswer != null && a.correctAnswer!.isNotEmpty) ...[
                const SizedBox(height: SsSpace.s1),
                Text('Correct answer: ${a.correctAnswer}', style: t.bodyMedium),
              ],
              if (a.explanation != null && a.explanation!.isNotEmpty) ...[
                const SizedBox(height: SsSpace.s1),
                Text(a.explanation!, style: t.bodySmall),
              ],
              if (a.feedback != null && a.feedback!.isNotEmpty) ...[
                const SizedBox(height: SsSpace.s1),
                Text('Feedback: ${a.feedback}', style: t.bodyMedium),
              ],
            ]),
          ),
          const SizedBox(height: SsSpace.s3),
        ],
      ],
    ]);
  }
}
