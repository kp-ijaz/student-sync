import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/app_services.dart';
import '../../core/models/dashboard_models.dart';
import '../../core/theme/status_styles.dart';
import '../../core/theme/tokens.dart';
import '../../core/widgets/async_view.dart';
import '../../core/widgets/common.dart';

/// Teacher class preparation: agenda, open doubts to address, and students who may
/// need attention (missed sessions, pending work, open doubts).
class PreparationSection extends StatelessWidget {
  const PreparationSection({super.key, required this.sessionId, this.onLoaded});
  final String sessionId;
  final VoidCallback? onLoaded;

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    return AsyncSection<ClassPreparation>(
      load: () => services.sessions.preparation(sessionId),
      onLoaded: onLoaded,
      builder: (context, prep, reload) {
        final t = Theme.of(context).textTheme;
        return Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const SectionHeader('Class preparation', icon: LucideIcons.listChecks, iconColor: SsColors.navy),
            Text('Agenda', style: t.titleSmall),
            const SizedBox(height: SsSpace.s2),
            if (prep.agenda.isEmpty)
              Text('No agenda items yet.', style: t.bodySmall)
            else
              SsCard(
                padding: EdgeInsets.zero,
                child: Column(
                  children: [
                    for (final a in prep.agenda)
                      ListTile(
                        leading: Icon(
                          a.isDiscussed ? LucideIcons.circleCheck : LucideIcons.circle,
                          color: a.isDiscussed ? SsColors.success : SsColors.slate,
                          semanticLabel: a.isDiscussed ? 'Discussed' : 'Not discussed yet',
                        ),
                        title: Text(a.title),
                        subtitle: a.studentName == null ? null : Text('Doubt from ${a.studentName}'),
                        onTap: a.doubtId == null ? null : () => context.push('/doubts/${a.doubtId}'),
                      ),
                  ],
                ),
              ),
            const SizedBox(height: SsSpace.s4),
            Text('Open doubts (${prep.openDoubts.length})', style: t.titleSmall),
            const SizedBox(height: SsSpace.s2),
            if (prep.openDoubts.isEmpty)
              Text('No open doubts from these students.', style: t.bodySmall)
            else
              for (final d in prep.openDoubts) ...[
                SsCard(
                  onTap: () => context.push('/doubts/${d.id}'),
                  child: Row(
                    children: [
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(d.title, style: t.titleSmall),
                            Text([d.studentName, if (d.subjectName != null) d.subjectName!].join(' · '), style: t.bodySmall),
                          ],
                        ),
                      ),
                      StatusBadge(StatusStyles.doubt(d.status), dense: true),
                    ],
                  ),
                ),
                const SizedBox(height: SsSpace.s2),
              ],
            const SizedBox(height: SsSpace.s3),
            Text('Students (${prep.students.length})', style: t.titleSmall),
            const SizedBox(height: SsSpace.s2),
            SsCard(
              padding: EdgeInsets.zero,
              child: Column(
                children: [
                  for (final s in prep.students)
                    ListTile(
                      title: Text(s.studentName),
                      subtitle: Text('${s.missedRecentSessions} missed recently · ${s.pendingAssignments} pending assignments · ${s.openDoubts} open doubts'),
                      trailing: (s.missedRecentSessions > 0 || s.openDoubts > 0)
                          ? const Tooltip(
                              message: 'May need attention',
                              child: Icon(LucideIcons.flag, color: SsColors.warning),
                            )
                          : null,
                    ),
                ],
              ),
            ),
            if (prep.students.isEmpty) const InlineEmpty(icon: LucideIcons.usersRound, text: 'No students in this class.'),
          ],
        );
      },
    );
  }
}
