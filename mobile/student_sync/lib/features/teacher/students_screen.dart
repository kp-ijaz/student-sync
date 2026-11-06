import 'dart:async';

import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/app_services.dart';
import '../../core/models/common.dart';
import '../../core/models/dashboard_models.dart';
import '../../core/theme/tokens.dart';
import '../../core/widgets/async_view.dart';
import '../../core/widgets/avatar.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/illustration.dart';
import '../../core/widgets/notification_bell.dart';
import '../../core/widgets/states.dart';

/// Students the teacher can see (the server scopes this to their classes).
class StudentsScreen extends StatefulWidget {
  const StudentsScreen({super.key});

  @override
  State<StudentsScreen> createState() => _StudentsScreenState();
}

class _StudentsScreenState extends State<StudentsScreen> {
  final _search = TextEditingController();
  Timer? _debounce;
  String _query = '';

  @override
  void dispose() {
    _debounce?.cancel();
    _search.dispose();
    super.dispose();
  }

  void _onSearch(String v) {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 400), () {
      if (mounted) setState(() => _query = v.trim());
    });
  }

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    return Scaffold(
      appBar: AppBar(title: const Text('Students'), actions: tabBarActions),
      body: Column(children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(SsSpace.s4, SsSpace.s3, SsSpace.s4, 0),
          child: TextField(
            controller: _search,
            onChanged: _onSearch,
            textInputAction: TextInputAction.search,
            decoration: const InputDecoration(labelText: 'Search students', prefixIcon: Icon(LucideIcons.search), hintText: 'Name, email or code'),
          ),
        ),
        Expanded(
          child: AsyncView<PagedResult<StudentSummary>>(
            key: ValueKey(_query),
            load: () => services.dashboards.students(search: _query, pageSize: 100),
            isEmpty: (p) => p.items.isEmpty,
            empty: (context, _) => EmptyState(
              icon: LucideIcons.usersRound,
              illustration: _query.isEmpty ? Illustration.empty : Illustration.search,
              title: _query.isEmpty ? 'No students yet' : 'No students match "$_query"',
            ),
            builder: (context, page, reload) {
              final t = Theme.of(context).textTheme;
              return RefreshIndicator(
                onRefresh: reload,
                child: ListView(padding: pagePadding(context).copyWith(top: SsSpace.s3), children: [
                  if (page.hasMore) Text('Showing ${page.items.length} of ${page.totalCount}. Search to narrow the list.', style: t.bodySmall),
                  const SizedBox(height: SsSpace.s2),
                  SsListGroup(children: [
                    for (final s in page.items)
                      ListTile(
                        contentPadding: const EdgeInsets.symmetric(horizontal: SsSpace.s4),
                        leading: UserAvatar(name: s.fullName, photoUrl: s.photoUrl, size: 40),
                        title: Text(s.fullName),
                        subtitle: Text([s.studentCode, if (s.gradeLevelName != null) s.gradeLevelName!].join(' · ')),
                        trailing: const Icon(LucideIcons.chevronRight, size: 18, color: SsColors.slate),
                        onTap: () => context.push('/students/${s.id}'),
                      ),
                  ]),
                ]),
              );
            },
          ),
        ),
      ]),
    );
  }
}

class StudentProfileScreen extends StatelessWidget {
  const StudentProfileScreen({super.key, required this.studentId});
  final String studentId;

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    return Scaffold(
      appBar: AppBar(title: const Text('Student')),
      body: AsyncView<StudentProfile>(
        load: () => services.dashboards.studentProfile(studentId),
        builder: (context, p, reload) {
          final t = Theme.of(context).textTheme;
          final s = p.student;
          return RefreshIndicator(
            onRefresh: reload,
            child: ListView(padding: pagePadding(context), children: [
              const SizedBox(height: SsSpace.s2),
              Semantics(header: true, child: Text(s.fullName, style: t.headlineSmall)),
              Text([s.studentCode, if (s.gradeLevelName != null) s.gradeLevelName!, if (s.schoolName != null) s.schoolName!].join(' · '), style: t.bodySmall),
              const SizedBox(height: SsSpace.s3),
              SsCard(
                child: Column(children: [
                  InfoRow(icon: LucideIcons.globe, label: 'Time zone', value: s.timeZoneId),
                  if (p.batches.isNotEmpty) InfoRow(icon: LucideIcons.usersRound, label: 'Batches', value: p.batches.map((b) => b.name).join(', ')),
                  if (p.arrangements.isNotEmpty)
                    InfoRow(icon: LucideIcons.handshake, label: 'One-to-one / private groups', value: p.arrangements.map((b) => b.name).join(', ')),
                  if (p.courses.isNotEmpty) InfoRow(icon: LucideIcons.bookOpen, label: 'Courses', value: p.courses.map((b) => b.name).join(', ')),
                ]),
              ),
              const SectionHeader('Progress'),
              for (final m in p.progress.all) ...[MetricTile(m), const SizedBox(height: SsSpace.s2)],
            ]),
          );
        },
      ),
    );
  }
}
