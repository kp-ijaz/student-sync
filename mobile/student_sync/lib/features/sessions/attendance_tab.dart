import 'package:flutter/material.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/app_services.dart';
import '../../core/models/enums.dart';
import '../../core/models/session_models.dart';
import '../../core/theme/status_styles.dart';
import '../../core/theme/tokens.dart';
import '../../core/widgets/async_view.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/states.dart';

/// Attendance for the class page: the teacher's editor, or a read-only list (a student
/// sees their own row). Hidden until there is something to show.
class AttendanceSection extends StatelessWidget {
  const AttendanceSection({super.key, required this.sessionId, this.onLoaded});
  final String sessionId;
  final VoidCallback? onLoaded;

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    return AsyncSection<AttendanceRoster>(
      load: () => services.sessions.attendance(sessionId),
      isEmpty: (r) => r.rows.isEmpty,
      onLoaded: onLoaded,
      builder: (context, roster, reload) => Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          SectionHeader(
            roster.canEdit ? 'Attendance' : (roster.rows.length == 1 ? 'Your attendance' : 'Attendance'),
            icon: LucideIcons.clipboardCheck,
            iconColor: SsColors.warning,
          ),
          if (roster.canEdit) AttendanceEditor(roster: roster, onSaved: reload) else _ReadOnlyAttendance(roster: roster),
        ],
      ),
    );
  }
}

class _ReadOnlyAttendance extends StatelessWidget {
  const _ReadOnlyAttendance({required this.roster});
  final AttendanceRoster roster;

  @override
  Widget build(BuildContext context) {
    final time = AppScope.of(context).time;
    return SsListGroup(
      children: [
        for (final r in roster.rows)
          ListTile(
            title: Text(r.studentName),
            subtitle: Text(
              [
                if (r.markedAtUtc != null) 'Marked ${time.dateTime(r.markedAtUtc!)}' else 'Not marked yet',
                if (r.note != null && r.note!.isNotEmpty) r.note!,
              ].join(' · '),
            ),
            trailing: StatusBadge(StatusStyles.attendance(r.status), dense: true),
          ),
      ],
    );
  }
}

/// Teacher attendance marking for one or many students in a single save.
class AttendanceEditor extends StatefulWidget {
  const AttendanceEditor({super.key, required this.roster, required this.onSaved});
  final AttendanceRoster roster;
  final Future<void> Function() onSaved;

  @override
  State<AttendanceEditor> createState() => _AttendanceEditorState();
}

class _AttendanceEditorState extends State<AttendanceEditor> {
  late Map<String, AttendanceStatus?> _status;
  late Map<String, String> _notes;
  bool _saving = false;

  @override
  void initState() {
    super.initState();
    _reset();
  }

  @override
  void didUpdateWidget(covariant AttendanceEditor oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.roster != widget.roster) _reset();
  }

  void _reset() {
    _status = {for (final r in widget.roster.rows) r.studentId: r.status};
    _notes = {for (final r in widget.roster.rows) r.studentId: r.note ?? ''};
  }

  List<AttendanceEntry> get _changes => [
    for (final r in widget.roster.rows)
      if (_status[r.studentId] != null && (_status[r.studentId] != r.status || (_notes[r.studentId] ?? '') != (r.note ?? '')))
        AttendanceEntry(
          studentId: r.studentId,
          status: _status[r.studentId]!,
          note: (_notes[r.studentId] ?? '').trim().isEmpty ? null : _notes[r.studentId]!.trim(),
        ),
  ];

  void _markAllUnmarked(AttendanceStatus status) {
    setState(() {
      for (final r in widget.roster.rows) {
        _status[r.studentId] ??= status;
      }
    });
  }

  Future<void> _save() async {
    final changes = _changes;
    if (changes.isEmpty) return;
    setState(() => _saving = true);
    try {
      await AppScope.read(context).sessions.markAttendance(widget.roster.sessionId, changes);
      if (!mounted) return;
      showSnack(context, 'Attendance saved for ${changes.length} ${changes.length == 1 ? 'student' : 'students'}.');
      await widget.onSaved();
    } on Object catch (e) {
      if (mounted) showSnack(context, describeError(e), error: true);
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  Future<void> _editNote(AttendanceRow row) async {
    final controller = TextEditingController(text: _notes[row.studentId]);
    final result = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text('Note for ${row.studentName}'),
        content: TextField(
          controller: controller,
          autofocus: true,
          maxLength: 500,
          decoration: const InputDecoration(labelText: 'Note (optional)', hintText: 'e.g. Arrived 10 minutes late'),
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancel')),
          FilledButton(onPressed: () => Navigator.pop(context, controller.text), child: const Text('Done')),
        ],
      ),
    );
    controller.dispose();
    if (result != null) setState(() => _notes[row.studentId] = result);
  }

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).textTheme;
    final changes = _changes.length;
    final unmarked = widget.roster.rows.where((r) => _status[r.studentId] == null).length;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            Expanded(child: Text('${widget.roster.rows.length} students · $unmarked not marked', style: t.bodySmall)),
            if (unmarked > 0) TextButton(onPressed: _saving ? null : () => _markAllUnmarked(AttendanceStatus.present), child: const Text('Mark rest present')),
          ],
        ),
        const SizedBox(height: SsSpace.s2),
        for (final r in widget.roster.rows) ...[
          SsCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Expanded(child: Text(r.studentName, style: t.titleSmall)),
                    IconButton(
                      tooltip: 'Add a note for ${r.studentName}',
                      onPressed: _saving ? null : () => _editNote(r),
                      icon: Icon((_notes[r.studentId] ?? '').isEmpty ? LucideIcons.filePlus : LucideIcons.notebookText, size: 20),
                    ),
                  ],
                ),
                if ((_notes[r.studentId] ?? '').isNotEmpty) Text(_notes[r.studentId]!, style: t.bodySmall),
                const SizedBox(height: SsSpace.s2),
                Wrap(
                  spacing: SsSpace.s2,
                  runSpacing: SsSpace.s2,
                  children: [
                    for (final status in AttendanceStatus.values)
                      ChoiceChip(
                        label: Text(StatusStyles.attendance(status).label),
                        selected: _status[r.studentId] == status,
                        showCheckmark: false,
                        selectedColor: StatusStyles.attendance(status).color.withValues(alpha: 0.18),
                        onSelected: _saving ? null : (_) => setState(() => _status[r.studentId] = status),
                        materialTapTargetSize: MaterialTapTargetSize.padded,
                        tooltip: '${r.studentName}: ${StatusStyles.attendance(status).label}',
                      ),
                  ],
                ),
              ],
            ),
          ),
          const SizedBox(height: SsSpace.s3),
        ],
        FilledButton.icon(
          onPressed: (_saving || changes == 0) ? null : _save,
          icon: _saving
              ? const SizedBox(width: 20, height: 20, child: CircularProgressIndicator(strokeWidth: 2, color: SsColors.white))
              : const Icon(LucideIcons.check, size: 18),
          label: Text(changes == 0 ? 'No changes to save' : 'Save attendance ($changes)'),
        ),
      ],
    );
  }
}
