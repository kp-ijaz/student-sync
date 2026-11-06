import 'package:flutter/material.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/app_services.dart';
import '../../core/models/common.dart';
import '../../core/models/dashboard_models.dart';
import '../../core/theme/tokens.dart';
import '../../core/widgets/async_view.dart';
import '../../core/widgets/common.dart';

/// The student's progress, opened from Profile: an attendance summary, then every
/// measure with how it is calculated.
class ProgressScreen extends StatelessWidget {
  const ProgressScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    return Scaffold(
      appBar: AppBar(title: const Text('My progress')),
      body: AsyncView<StudentDashboard>(
        load: services.dashboards.student,
        loadingLabel: 'Loading progress',
        builder: (context, d, reload) {
          final p = d.progress;
          final t = Theme.of(context).textTheme;
          return RefreshIndicator(
            onRefresh: reload,
            child: ListView(padding: pagePadding(context), children: [
              const SizedBox(height: SsSpace.s2),
              _Summary(progress: p),
              const SectionHeader('All measures', icon: LucideIcons.chartLine, iconColor: SsColors.success),
              Text('Tap a measure to see how it is calculated.', style: t.bodySmall),
              const SizedBox(height: SsSpace.s3),
              for (final m in p.all) ...[MetricTile(m), const SizedBox(height: SsSpace.s2)],
            ]),
          );
        },
      ),
    );
  }
}

/// Attendance as a ring, with lessons attended and active weeks beside it.
class _Summary extends StatelessWidget {
  const _Summary({required this.progress});
  final Progress progress;

  @override
  Widget build(BuildContext context) {
    final attendance = progress.attendance;
    final pct = attendance.percent;
    return HeroCard(
      semanticLabel: 'Attendance ${attendance.value}. ${progress.lessonsAttended.label}: ${progress.lessonsAttended.value}. '
          '${progress.activeWeeks.label}: ${progress.activeWeeks.value}.',
      child: ExcludeSemantics(
        child: Row(children: [
          SizedBox.square(
            dimension: 104,
            child: Stack(fit: StackFit.expand, children: [
              CircularProgressIndicator(
                value: pct == null ? 0 : (pct / 100).clamp(0, 1),
                strokeWidth: 10,
                strokeCap: StrokeCap.round,
                color: SsColors.orange,
                backgroundColor: const Color(0x33FFFFFF),
              ),
              Center(
                child: Column(mainAxisSize: MainAxisSize.min, children: [
                  Text(attendance.value,
                      style: const TextStyle(fontFamily: 'PlusJakartaSans', fontSize: 22, fontWeight: FontWeight.w700, color: SsColors.white)),
                  const Text('Attendance', style: TextStyle(fontFamily: 'Inter', fontSize: 11, color: Color(0xD9FFFFFF))),
                ]),
              ),
            ]),
          ),
          const SizedBox(width: SsSpace.s5),
          Expanded(
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              _Figure(value: progress.lessonsAttended.value, label: progress.lessonsAttended.label),
              const SizedBox(height: SsSpace.s3),
              _Figure(value: progress.activeWeeks.value, label: progress.activeWeeks.label),
            ]),
          ),
        ]),
      ),
    );
  }
}

class _Figure extends StatelessWidget {
  const _Figure({required this.value, required this.label});
  final String value;
  final String label;

  @override
  Widget build(BuildContext context) => Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Text(value,
            style: const TextStyle(
              fontFamily: 'PlusJakartaSans',
              fontSize: 24,
              fontWeight: FontWeight.w700,
              color: SsColors.white,
              fontFeatures: [FontFeature.tabularFigures()],
            )),
        Text(label, style: const TextStyle(fontFamily: 'Inter', fontSize: 13, color: Color(0xD9FFFFFF))),
      ]);
}
