import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/app_services.dart';
import '../../core/models/learning_models.dart';
import '../../core/theme/status_styles.dart';
import '../../core/theme/tokens.dart';
import '../../core/time/time_format.dart';
import '../../core/widgets/common.dart';

class AssignmentCard extends StatelessWidget {
  const AssignmentCard({super.key, required this.assignment, this.showSubmission = true, this.navigable = true});
  final AssignmentListItem assignment;
  final bool showSubmission;

  /// Parents see a read-only summary; the detail screen is the student's workspace.
  final bool navigable;

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).textTheme;
    final time = AppScope.of(context).time;
    final a = assignment;
    final overdue = a.dueAtUtc.isBefore(DateTime.now().toUtc()) && a.mySubmissionStatus == null;
    return SsCard(
      onTap: navigable ? () => context.push('/assignments/${a.id}') : null,
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          IconTile(LucideIcons.clipboardList, color: overdue ? SsColors.error : SsColors.success),
          const SizedBox(width: SsSpace.s3),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Expanded(child: Text(a.title, style: t.titleSmall)),
                    const SizedBox(width: SsSpace.s2),
                    if (showSubmission) StatusBadge(StatusStyles.submission(a.mySubmissionStatus), dense: true),
                  ],
                ),
                const SizedBox(height: 2),
                Text(
                  [
                    'Due ${time.dateTime(a.dueAtUtc)}',
                    if (a.myMarks != null) '${formatMarks(a.myMarks!)}/${formatMarks(a.maxMarks)} marks' else '${formatMarks(a.maxMarks)} marks',
                  ].join(' · '),
                  style: t.bodySmall?.copyWith(color: overdue ? SsColors.error : null, fontWeight: overdue ? FontWeight.w600 : null),
                ),
                Text([a.teacherName, if (a.sessionTitle != null) a.sessionTitle!].join(' · '), style: t.labelSmall),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
