import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/app_services.dart';
import '../../core/models/doubt_models.dart';
import '../../core/theme/status_styles.dart';
import '../../core/theme/tokens.dart';
import '../../core/widgets/common.dart';

class DoubtCard extends StatelessWidget {
  const DoubtCard({super.key, required this.doubt, this.showStudent = true, this.onReturn});
  final DoubtListItem doubt;
  final bool showStudent;

  /// Called after returning from the detail screen (e.g. to refresh the list).
  final Future<void> Function()? onReturn;

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).textTheme;
    final time = AppScope.of(context).time;
    final d = doubt;
    return SsCard(
      onTap: () async {
        await context.push('/doubts/${d.id}');
        await onReturn?.call();
      },
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const IconTile(LucideIcons.messageCircleQuestionMark, color: SsColors.orangeDeep),
          const SizedBox(width: SsSpace.s3),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Expanded(child: Text(d.title, style: t.titleSmall)),
                    const SizedBox(width: SsSpace.s2),
                    StatusBadge(StatusStyles.doubt(d.status), dense: true),
                  ],
                ),
                const SizedBox(height: SsSpace.s1),
                Text(
                  [
                    if (showStudent) d.studentName,
                    if (d.subjectName != null) d.subjectName!,
                    if (d.assignedTeacherName != null) 'Teacher: ${d.assignedTeacherName}',
                    '${d.replyCount} ${d.replyCount == 1 ? 'reply' : 'replies'}',
                  ].join(' · '),
                  style: t.bodySmall,
                ),
                if (d.followUpSessionStartUtc != null)
                  Padding(
                    padding: const EdgeInsets.only(top: SsSpace.s1),
                    child: Text(
                      'To be discussed in class on ${time.dateTime(d.followUpSessionStartUtc!)}',
                      style: t.labelSmall?.copyWith(color: SsColors.navy),
                    ),
                  ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
