import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/app_services.dart';
import '../../core/models/subject_models.dart';
import '../../core/theme/tokens.dart';
import '../../core/widgets/async_view.dart';
import '../../core/widgets/avatar.dart';
import '../../core/widgets/class_cover.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/notification_bell.dart';
import '../../core/widgets/session_card.dart';
import '../../core/widgets/states.dart';

/// Student "Subjects" tab: one card per subject the student has classes in, with the
/// syllabus progress, teachers and next class. Opens the subject's syllabus.
class SubjectsScreen extends StatelessWidget {
  const SubjectsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    return Scaffold(
      appBar: AppBar(
        title: const Text('My subjects'),
        actions: [
          IconButton(
            tooltip: 'Timetable',
            style: IconButton.styleFrom(backgroundColor: SsColors.white, side: const BorderSide(color: SsColors.hairline)),
            icon: const Icon(LucideIcons.calendarDays, size: 20),
            onPressed: () => context.push('/schedule'),
          ),
          const SizedBox(width: SsSpace.s2),
          ...tabBarActions,
        ],
      ),
      body: AsyncView<List<MySubject>>(
        load: services.subjects.mine,
        isEmpty: (s) => s.isEmpty,
        empty: (context, _) => const EmptyState(
          icon: LucideIcons.bookOpen,
          title: 'No subjects yet',
          message: 'Your subjects appear here once the institute adds you to your class (standard).',
        ),
        builder: (context, subjects, reload) {
          List<MySubject> of(SubjectKind k) => subjects.where((s) => s.kind == k).toList();
          final groups = [
            ('Core subjects', LucideIcons.bookOpen, SsColors.blue, of(SubjectKind.core)),
            ('Languages', LucideIcons.languages, SsColors.orangeDeep, of(SubjectKind.language)),
            ('Additional subjects', LucideIcons.circlePlus, SsColors.success, of(SubjectKind.additional)),
          ];
          return RefreshIndicator(
            onRefresh: reload,
            child: ListView(padding: pagePadding(context), children: [
              for (final (title, icon, color, items) in groups)
                if (items.isNotEmpty) ...[
                  SectionHeader(title, icon: icon, iconColor: color, count: items.length),
                  GridView.count(
                    crossAxisCount: 2,
                    shrinkWrap: true,
                    padding: EdgeInsets.zero,
                    physics: const NeverScrollableScrollPhysics(),
                    mainAxisSpacing: SsSpace.s3,
                    crossAxisSpacing: SsSpace.s3,
                    childAspectRatio: 0.74,
                    children: [
                      for (final subject in items) SubjectTile(subject: subject, onTap: () => context.push('/subjects/${subject.subjectId}')),
                    ],
                  ),
                ],
            ]),
          );
        },
      ),
    );
  }
}

/// A subject in the grid: its cover photo and name, the teacher, syllabus progress and
/// the next class.
class SubjectTile extends StatelessWidget {
  const SubjectTile({super.key, required this.subject, required this.onTap});
  final MySubject subject;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final s = subject;
    final t = Theme.of(context).textTheme;
    final time = AppScope.of(context).time;
    final next = s.nextSession;
    return SsCard(
      onTap: onTap,
      padding: const EdgeInsets.all(SsSpace.s2),
      semanticLabel: [
        s.name,
        if (s.group != null) s.group!,
        if (s.chapterCount > 0) '${s.completedChapterCount} of ${s.chapterCount} chapters done',
        if (next != null) 'next class ${time.dateTime(next.startUtc)}',
      ].join(', '),
      child: ExcludeSemantics(
        child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          Expanded(
            child: ClassCoverImage(
              subject: s.name,
              title: s.courseName,
              radius: SsRadius.control,
              child: Padding(
                padding: const EdgeInsets.all(SsSpace.s2),
                child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  if (s.group != null) GlassPill(label: s.group!),
                  const Spacer(),
                  Text(s.name,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(fontFamily: 'PlusJakartaSans', fontSize: 17, height: 1.2, fontWeight: FontWeight.w700, color: SsColors.white)),
                ]),
              ),
            ),
          ),
          Padding(
            padding: const EdgeInsets.fromLTRB(SsSpace.s1, SsSpace.s2, SsSpace.s1, 0),
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Row(children: [
                if (s.teachers.isNotEmpty) ...[TeacherStack(teachers: s.teachers, size: 22), const SizedBox(width: 6)],
                Expanded(
                  child: Text(
                    s.teachers.isEmpty ? 'Teacher to be assigned' : _shortName(s.teachers.first.name),
                    style: t.labelSmall?.copyWith(color: SsColors.textPrimary, fontWeight: FontWeight.w600),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
              ]),
              const SizedBox(height: SsSpace.s2),
              ClipRRect(
                borderRadius: BorderRadius.circular(SsRadius.pill),
                child: LinearProgressIndicator(value: s.progress.clamp(0, 1), minHeight: 6, color: SsColors.blue, backgroundColor: SsColors.fill),
              ),
              const SizedBox(height: 6),
              Text(
                next == null ? 'No class scheduled' : 'Next: ${time.dateTime(next.startUtc)}',
                style: t.labelSmall,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
              ),
            ]),
          ),
        ]),
      ),
    );
  }
}

/// "Amira Haddad (Demo Teacher)" → "Amira Haddad".
String _shortName(String name) => name.replaceAll(RegExp(r'\s*\([^)]*\)'), '').trim();

/// Overlapping teacher photos (at most three).
class TeacherStack extends StatelessWidget {
  const TeacherStack({super.key, required this.teachers, this.size = 32});
  final List<SubjectTeacher> teachers;
  final double size;

  @override
  Widget build(BuildContext context) {
    final shown = teachers.take(3).toList();
    if (shown.isEmpty) return const SizedBox.shrink();
    return SizedBox(
      width: size + (shown.length - 1) * size * 0.65,
      height: size,
      child: Stack(children: [
        for (var i = 0; i < shown.length; i++)
          Positioned(left: i * size * 0.65, child: UserAvatar(name: shown[i].name, photoUrl: shown[i].photoUrl, size: size)),
      ]),
    );
  }
}

/// "3 of 5 chapters done" with a progress bar.
class SyllabusProgress extends StatelessWidget {
  const SyllabusProgress({super.key, required this.done, required this.total, required this.value});
  final int done;
  final int total;
  final double value;

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).textTheme;
    return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
      Row(children: [
        Text('Syllabus', style: t.labelMedium?.copyWith(fontWeight: FontWeight.w600, color: SsColors.textPrimary)),
        const Spacer(),
        Text(total == 0 ? 'Not set yet' : '$done of $total chapters done', style: t.labelSmall),
      ]),
      const SizedBox(height: SsSpace.s2),
      ClipRRect(
        borderRadius: BorderRadius.circular(SsRadius.pill),
        child: LinearProgressIndicator(value: value.clamp(0, 1), minHeight: 8, color: SsColors.blue, backgroundColor: SsColors.fill),
      ),
    ]);
  }
}
