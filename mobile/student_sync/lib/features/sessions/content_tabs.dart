import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/app_services.dart';
import '../../core/models/common.dart';
import '../../core/models/content_models.dart';
import '../../core/models/learning_models.dart';
import '../../core/models/enums.dart';
import '../../core/theme/status_styles.dart';
import '../../core/theme/tokens.dart';
import '../../core/time/time_format.dart';
import '../../core/widgets/async_view.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/states.dart';
import '../learning/assignment_card.dart';

/// Published notes (students) or all notes the teacher can manage, as Markdown.
/// A section of the class page; hidden while there are none.
class NotesSection extends StatelessWidget {
  const NotesSection({super.key, required this.sessionId, required this.canManage, this.onLoaded});
  final String sessionId;
  final bool canManage;
  final VoidCallback? onLoaded;

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    return AsyncSection<List<Note>>(
      load: () => services.sessions.notes(sessionId),
      isEmpty: (n) => n.isEmpty,
      onLoaded: onLoaded,
      builder: (context, notes, reload) => Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        SectionHeader('Class notes', icon: LucideIcons.notebookText, count: notes.length),
        for (final n in notes) ...[_NoteCard(note: n, showStatus: canManage), const SizedBox(height: SsSpace.s3)],
      ]),
    );
  }
}

class _NoteCard extends StatelessWidget {
  const _NoteCard({required this.note, required this.showStatus});
  final Note note;
  final bool showStatus;

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).textTheme;
    final time = AppScope.of(context).time;
    final when = note.publishedAtUtc ?? note.updatedAtUtc;
    return SsCard(
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Expanded(child: Semantics(header: true, child: Text(note.title, style: t.titleMedium))),
          if (showStatus) StatusBadge(StatusStyles.publication(note.status), dense: true),
        ]),
        const SizedBox(height: SsSpace.s1),
        Text('${note.authorName} · ${time.dateTime(when)}', style: t.bodySmall),
        if (note.source == ContentSource.aiApproved) ...[
          const SizedBox(height: SsSpace.s2),
          const TagPill('AI-assisted · reviewed by your teacher', icon: LucideIcons.badgeCheck),
        ],
        if (showStatus && note.status == PublicationStatus.draft) ...[
          const SizedBox(height: SsSpace.s2),
          Text('Draft — not visible to students until published (publish from the web app).', style: t.bodySmall?.copyWith(color: SsColors.navy)),
        ],
        const Divider(height: SsSpace.s5),
        MarkdownView(note.bodyMarkdown),
      ]),
    );
  }
}

/// Assignments set for this class. A section of the class page; hidden while empty.
class AssignmentsSection extends StatelessWidget {
  const AssignmentsSection({super.key, required this.sessionId, required this.forTeacher, this.onLoaded});
  final String sessionId;
  final bool forTeacher;
  final VoidCallback? onLoaded;

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    return AsyncSection<PagedResult<AssignmentListItem>>(
      load: () => services.learning.assignments(sessionId: sessionId),
      isEmpty: (p) => p.items.isEmpty,
      onLoaded: onLoaded,
      builder: (context, page, reload) => Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        SectionHeader('Assignments', icon: LucideIcons.clipboardList, iconColor: SsColors.success, count: page.items.length),
        for (final a in page.items) ...[AssignmentCard(assignment: a, showSubmission: !forTeacher), const SizedBox(height: SsSpace.s3)],
      ]),
    );
  }
}

/// Files and links shared for the class. A section of the class page; hidden while empty.
class MaterialsSection extends StatelessWidget {
  const MaterialsSection({super.key, required this.sessionId, this.onLoaded});
  final String sessionId;
  final VoidCallback? onLoaded;

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    return AsyncSection<List<LearningMaterial>>(
      load: () => services.sessions.materials(sessionId),
      isEmpty: (m) => m.isEmpty,
      onLoaded: onLoaded,
      builder: (context, items, reload) => Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        SectionHeader('Materials', icon: LucideIcons.folderOpen, iconColor: SsColors.success, count: items.length),
        for (final m in items) ...[MaterialTile(material: m), const SizedBox(height: SsSpace.s3)],
      ]),
    );
  }
}

class MaterialTile extends StatefulWidget {
  const MaterialTile({super.key, required this.material});
  final LearningMaterial material;

  @override
  State<MaterialTile> createState() => _MaterialTileState();
}

class _MaterialTileState extends State<MaterialTile> {
  bool _opening = false;

  Future<void> _open() async {
    final m = widget.material;
    if (m.kind == MaterialKind.link) {
      if (m.url != null) await openExternal(context, m.url!);
      return;
    }
    final file = m.file;
    if (file == null) return;
    setState(() => _opening = true);
    try {
      final services = AppScope.read(context);
      // Private files are served through a short-lived, user-bound link.
      final link = await services.sessions.fileLink(file.id);
      if (!mounted) return;
      await openExternal(context, services.config.resolveServerPath(link.url).toString(), inApp: true);
    } on Object catch (e) {
      if (mounted) showSnack(context, describeError(e), error: true);
    } finally {
      if (mounted) setState(() => _opening = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final m = widget.material;
    final t = Theme.of(context).textTheme;
    final isLink = m.kind == MaterialKind.link;
    final detail = isLink ? (Uri.tryParse(m.url ?? '')?.host ?? 'Link') : '${m.file?.fileName ?? 'File'} · ${_size(m.file?.sizeBytes ?? 0)}';
    return SsCard(
      onTap: _opening ? null : _open,
      semanticLabel: '${isLink ? 'Open link' : 'Open file'}: ${m.title}',
      child: Row(children: [
        IconTile(isLink ? LucideIcons.link : LucideIcons.fileText, color: SsColors.success),
        const SizedBox(width: SsSpace.s3),
        Expanded(
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text(m.title, style: t.titleSmall),
            if (m.description != null && m.description!.isNotEmpty) Text(m.description!, style: t.bodySmall),
            Text(detail, style: t.labelSmall),
          ]),
        ),
        _opening
            ? const SizedBox(width: 20, height: 20, child: CircularProgressIndicator(strokeWidth: 2))
            : const ExcludeSemantics(child: Icon(LucideIcons.externalLink, size: 20, color: SsColors.slate)),
      ]),
    );
  }

  static String _size(int bytes) {
    if (bytes < 1024) return '$bytes B';
    if (bytes < 1024 * 1024) return '${(bytes / 1024).toStringAsFixed(0)} KB';
    return '${(bytes / (1024 * 1024)).toStringAsFixed(1)} MB';
  }
}

/// Class recordings. Available recordings with a stored file play in-app; students'
/// watch position is saved by the player. A section of the class page; hidden while empty.
class RecordingsSection extends StatelessWidget {
  const RecordingsSection({super.key, required this.sessionId, this.onLoaded});
  final String sessionId;
  final VoidCallback? onLoaded;

  static String _playLabel(Recording r) =>
      !r.completed && (r.resumePositionSeconds ?? 0) > 0 ? 'Resume from ${formatDuration(Duration(seconds: r.resumePositionSeconds!))}' : 'Play';

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    return AsyncSection<List<Recording>>(
      load: () => services.sessions.recordings(sessionId),
      isEmpty: (r) => r.isEmpty,
      onLoaded: onLoaded,
      builder: (context, items, reload) {
        final t = Theme.of(context).textTheme;
        final time = services.time;
        return Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          SectionHeader('Recordings', icon: LucideIcons.clapperboard, iconColor: SsColors.navy, count: items.length),
          for (final r in items) ...[
            SsCard(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Row(children: [
                  const IconTile(LucideIcons.clapperboard, color: SsColors.navy),
                  const SizedBox(width: SsSpace.s3),
                  Expanded(child: Text(r.title, style: t.titleSmall)),
                  StatusBadge(StatusStyles.recording(r.status), dense: true),
                ]),
                const SizedBox(height: SsSpace.s2),
                Text(
                  [
                    if (r.durationSeconds != null) formatDuration(Duration(seconds: r.durationSeconds!)),
                    'Added ${time.date(r.createdAtUtc)}',
                    'Available until ${time.date(r.retainUntilUtc)}',
                    if (r.completed) 'Watched',
                  ].join(' · '),
                  style: t.bodySmall,
                ),
                if (r.isPlayable) ...[
                  const SizedBox(height: SsSpace.s3),
                  FilledButton.icon(
                    icon: const Icon(LucideIcons.play, size: 18),
                    label: Text(_playLabel(r)),
                    onPressed: () async {
                      await context.push('/recordings/${r.id}/play', extra: r);
                      await reload();
                    },
                  ),
                ],
              ]),
            ),
            const SizedBox(height: SsSpace.s3),
          ],
        ]);
      },
    );
  }
}
