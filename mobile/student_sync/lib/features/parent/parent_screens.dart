import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/app_services.dart';
import '../../core/models/common.dart';
import '../../core/models/dashboard_models.dart';
import '../../core/theme/status_styles.dart';
import '../../core/theme/tokens.dart';
import '../../core/time/time_format.dart';
import '../../core/widgets/async_view.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/notification_bell.dart';
import '../../core/widgets/session_card.dart';
import '../../core/widgets/states.dart';
import '../learning/assignment_card.dart';

/// Loads `GET /dashboard/parent?childId=` for the child selected in the shared
/// switcher and rebuilds when the selection changes.
class ParentDashboardView extends StatelessWidget {
  const ParentDashboardView({super.key, required this.builder});
  final Widget Function(BuildContext context, ParentDashboard dashboard, ChildOverview child, Future<void> Function() reload) builder;

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    return ValueListenableBuilder<String?>(
      valueListenable: services.parentChild,
      builder: (context, childId, _) => AsyncView<ParentDashboard>(
        key: ValueKey(childId),
        // With no explicit choice the server selects the default child.
        load: () => services.dashboards.parent(childId: childId),
        isEmpty: (d) => d.children.isEmpty || d.child == null,
        empty: (context, _) => const EmptyState(
          icon: LucideIcons.usersRound,
          title: 'No children linked yet',
          message: 'Ask your institution to link your account to your child. Linked children appear here.',
        ),
        builder: (context, d, reload) => builder(context, d, d.child!, reload),
      ),
    );
  }
}

class ChildSwitcher extends StatelessWidget {
  const ChildSwitcher({super.key, required this.children, required this.selectedId});
  final List<LookupItem> children;
  final String selectedId;

  @override
  Widget build(BuildContext context) {
    if (children.length < 2) return const SizedBox.shrink();
    final services = AppScope.of(context);
    return Padding(
      padding: const EdgeInsets.only(top: SsSpace.s2),
      child: Semantics(
        label: 'Choose child',
        child: Wrap(spacing: SsSpace.s2, runSpacing: SsSpace.s2, children: [
          for (final c in children)
            ChoiceChip(
              label: Text(_shortName(c.name)),
              selected: c.id == selectedId,
              onSelected: (_) => services.parentChild.value = c.id,
              materialTapTargetSize: MaterialTapTargetSize.padded,
            ),
        ]),
      ),
    );
  }
}

/// "Fatima Al Mansoori (Demo Student)" → "Fatima Al Mansoori"
String _shortName(String name) => name.replaceAll(RegExp(r'\s*\(.*\)$'), '');

class ParentHomeScreen extends StatelessWidget {
  const ParentHomeScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    return Scaffold(
      appBar: AppBar(title: const Text('Home'), actions: tabBarActions),
      body: ParentDashboardView(builder: (context, d, child, reload) {
        final t = Theme.of(context).textTheme;
        final time = services.time;
        final next = child.upcomingSessions.firstOrNull;
        return RefreshIndicator(
          onRefresh: () async {
            await reload();
            await services.unread.refresh();
          },
          child: ListView(padding: pagePadding(context), children: [
            const SizedBox(height: SsSpace.s2),
            Text('Hello ${services.auth.user?.firstName ?? ''}', style: t.bodyMedium?.copyWith(color: SsColors.textSecondary)),
            Semantics(header: true, child: Text(_shortName(child.name), style: t.headlineMedium)),
            ChildSwitcher(children: d.children, selectedId: child.studentId),
            const SectionHeader('Next class'),
            if (next == null)
              Text('No upcoming classes.', style: t.bodySmall)
            else
              SessionCard(session: next, time: time, highlight: true, onTap: () => context.push('/sessions/${next.id}')),
            const SectionHeader('At a glance'),
            StatGrid(children: [
              StatCard(label: child.progress.attendance.label, value: child.progress.attendance.value),
              StatCard(label: child.progress.assignmentsSubmitted.label, value: child.progress.assignmentsSubmitted.value),
              StatCard(label: child.progress.averageAssignmentMark.label, value: child.progress.averageAssignmentMark.value),
              StatCard(label: child.progress.openDoubts.label, value: child.progress.openDoubts.value),
            ]),
            const SizedBox(height: SsSpace.s2),
            Align(
              alignment: Alignment.centerLeft,
              child: TextButton(onPressed: () => context.go('/parent/progress'), child: const Text('How these are calculated')),
            ),
            SectionHeader('Recent attendance'),
            if (child.recentAttendance.isEmpty)
              Text('No attendance recorded yet.', style: t.bodySmall)
            else
              SsCard(
                padding: EdgeInsets.zero,
                child: Column(children: [
                  for (final a in child.recentAttendance.take(5))
                    ListTile(
                      title: Text(a.sessionTitle),
                      subtitle: Text('${time.dateTime(a.startUtc)} · ${StatusStyles.sessionTypeLabel(a.sessionType)}'),
                      trailing: StatusBadge(StatusStyles.attendance(a.status), dense: true),
                    ),
                ]),
              ),
            const SectionHeader('Assignments'),
            if (child.assignments.isEmpty)
              Text('No assignments.', style: t.bodySmall)
            else
              for (final a in child.assignments.take(5)) ...[
                AssignmentCard(assignment: a, navigable: false),
                const SizedBox(height: SsSpace.s3),
              ],
          ]),
        );
      }),
    );
  }
}

class ParentChildrenScreen extends StatelessWidget {
  const ParentChildrenScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    return Scaffold(
      appBar: AppBar(title: const Text('Children'), actions: tabBarActions),
      body: AsyncView<List<LookupItem>>(
        load: services.dashboards.myChildren,
        isEmpty: (c) => c.isEmpty,
        empty: (context, _) => const EmptyState(
          icon: LucideIcons.usersRound,
          title: 'No children linked yet',
          message: 'Ask your institution to link your account to your child.',
        ),
        builder: (context, children, reload) => RefreshIndicator(
          onRefresh: reload,
          child: ValueListenableBuilder<String?>(
            valueListenable: services.parentChild,
            builder: (context, chosen, _) {
              final selected = chosen ?? children.first.id;
              return ListView(padding: pagePadding(context), children: [
              const SizedBox(height: SsSpace.s2),
              for (final c in children) ...[
                SsCard(
                  onTap: () {
                    services.parentChild.value = c.id;
                    context.go('/parent/home');
                  },
                  accent: c.id == selected ? SsColors.blue : null,
                  semanticLabel: 'View ${_shortName(c.name)}${c.id == selected ? ', currently selected' : ''}',
                  child: Row(children: [
                    CircleAvatar(
                      backgroundColor: SsColors.blue50,
                      child: Text(_shortName(c.name).characters.first, style: const TextStyle(color: SsColors.navy, fontWeight: FontWeight.w600)),
                    ),
                    const SizedBox(width: SsSpace.s3),
                    Expanded(
                      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                        Text(_shortName(c.name), style: Theme.of(context).textTheme.titleSmall),
                        if (c.detail != null) Text('Student code ${c.detail}', style: Theme.of(context).textTheme.bodySmall),
                      ]),
                    ),
                    if (c.id == selected) const TagPill('Selected'),
                    const ExcludeSemantics(child: Icon(LucideIcons.chevronRight, color: SsColors.slate)),
                  ]),
                ),
                const SizedBox(height: SsSpace.s3),
              ],
            ]);
            },
          ),
        ),
      ),
    );
  }
}

class ParentCalendarScreen extends StatelessWidget {
  const ParentCalendarScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    return Scaffold(
      appBar: AppBar(title: const Text('Calendar'), actions: tabBarActions),
      body: ParentDashboardView(builder: (context, d, child, reload) {
        final t = Theme.of(context).textTheme;
        final time = services.time;
        final now = DateTime.now().toUtc();
        final items = <Widget>[];
        String? lastKey;
        for (final s in child.upcomingSessions) {
          final key = time.dayKey(s.startUtc);
          if (key != lastKey) {
            items.add(SectionHeader(time.dayLabel(s.startUtc, now)));
            lastKey = key;
          }
          items.add(Padding(
            padding: const EdgeInsets.only(bottom: SsSpace.s3),
            child: SessionCard(session: s, time: time, onTap: () => context.push('/sessions/${s.id}')),
          ));
        }
        return RefreshIndicator(
          onRefresh: reload,
          child: ListView(padding: pagePadding(context), children: [
            const SizedBox(height: SsSpace.s2),
            Text("${_shortName(child.name)}'s upcoming classes", style: t.titleMedium),
            Text('Times shown in ${time.viewerZoneId}.', style: t.bodySmall),
            ChildSwitcher(children: d.children, selectedId: child.studentId),
            if (items.isEmpty) const EmptyState(icon: LucideIcons.calendarCheck, title: 'No upcoming classes') else ...items,
          ]),
        );
      }),
    );
  }
}

class ParentProgressScreen extends StatelessWidget {
  const ParentProgressScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    return Scaffold(
      appBar: AppBar(title: const Text('Progress'), actions: tabBarActions),
      body: ParentDashboardView(builder: (context, d, child, reload) {
        final t = Theme.of(context).textTheme;
        final time = services.time;
        return RefreshIndicator(
          onRefresh: reload,
          child: ListView(padding: pagePadding(context), children: [
            const SizedBox(height: SsSpace.s2),
            Semantics(header: true, child: Text(_shortName(child.name), style: t.headlineSmall)),
            ChildSwitcher(children: d.children, selectedId: child.studentId),
            const SizedBox(height: SsSpace.s2),
            Text('Each measure comes from recorded attendance, submissions and released results. Tap one to see how it is calculated.', style: t.bodySmall),
            const SizedBox(height: SsSpace.s3),
            for (final m in child.progress.all) ...[MetricTile(m), const SizedBox(height: SsSpace.s2)],
            const SectionHeader('Exam results'),
            if (child.results.isEmpty)
              Text('No released exam results yet.', style: t.bodySmall)
            else
              SsCard(
                padding: EdgeInsets.zero,
                child: Column(children: [
                  for (final r in child.results)
                    ListTile(
                      title: Text(r.examTitle),
                      subtitle: r.submittedAtUtc == null ? null : Text('Submitted ${time.date(r.submittedAtUtc!)}'),
                      trailing: Text(
                        r.score == null ? 'Pending' : '${formatMarks(r.score!)}/${formatMarks(r.totalMarks)}',
                        style: t.titleSmall?.copyWith(fontFeatures: const [FontFeature.tabularFigures()]),
                      ),
                    ),
                ]),
              ),
          ]),
        );
      }),
    );
  }
}
