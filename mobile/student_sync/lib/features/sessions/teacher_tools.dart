import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:image_picker/image_picker.dart';
import 'package:intl/intl.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/api/api_exception.dart';
import '../../core/app_services.dart';
import '../../core/models/enums.dart';
import '../../core/models/session_models.dart';
import '../../core/theme/tokens.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/states.dart';
import '../videos/video_widgets.dart' show unlockOffsetLabel;

/// "Add to this class" for the class teacher: a note, an assignment, or a video.
/// [onAdded] refreshes the class page after something was added.
class TeacherToolsCard extends StatelessWidget {
  const TeacherToolsCard({super.key, required this.session, required this.onAdded});
  final SessionDetail session;
  final Future<void> Function() onAdded;

  Future<void> _open(BuildContext context, Widget screen) async {
    final added = await Navigator.of(context).push<bool>(MaterialPageRoute(builder: (_) => screen, fullscreenDialog: true));
    if (added == true) await onAdded();
  }

  /// Notes and assignments come out of the class, so they open once it has ended.
  bool get _ended => session.status == SessionStatus.completed || !session.endUtc.isAfter(DateTime.now().toUtc());

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).textTheme;
    final ended = _ended && session.status != SessionStatus.cancelled;
    return SsCard(
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Text('Add to this class', style: t.titleMedium),
        Text(
          ended
              ? 'Add the key points and any homework from the class. AI-drafted notes from the class recording wait for your review in the web app.'
              : 'Notes and assignments open once the class has ended. You can upload a video now (it unlocks when you choose).',
          style: t.bodySmall,
        ),
        const SizedBox(height: SsSpace.s3),
        Row(children: [
          Expanded(
            child: _ToolButton(
              icon: LucideIcons.notebookPen,
              label: 'Note',
              color: SsColors.blue,
              onTap: ended ? () => _open(context, AddNoteScreen(session: session)) : null,
            ),
          ),
          const SizedBox(width: SsSpace.s2),
          Expanded(
            child: _ToolButton(
              icon: LucideIcons.clipboardList,
              label: 'Assignment',
              color: SsColors.success,
              onTap: ended ? () => _open(context, AddAssignmentScreen(session: session)) : null,
            ),
          ),
          const SizedBox(width: SsSpace.s2),
          Expanded(child: _ToolButton(icon: LucideIcons.clapperboard, label: 'Video', color: SsColors.navy, onTap: () => _open(context, UploadVideoScreen(session: session)))),
        ]),
      ]),
    );
  }
}

class _ToolButton extends StatelessWidget {
  const _ToolButton({required this.icon, required this.label, required this.color, required this.onTap});
  final IconData icon;
  final String label;
  final Color color;

  /// Null while the action is not available yet (shown greyed out).
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) => Semantics(
        button: true,
        enabled: onTap != null,
        label: 'Add $label',
        excludeSemantics: true,
        child: Material(
          color: onTap == null ? SsColors.fill : color.withValues(alpha: 0.08),
          borderRadius: BorderRadius.circular(SsRadius.control),
          child: InkWell(
            borderRadius: BorderRadius.circular(SsRadius.control),
            onTap: onTap,
            child: Padding(
              padding: const EdgeInsets.symmetric(vertical: SsSpace.s3),
              child: Column(children: [
                Icon(icon, color: onTap == null ? SsColors.slate : color, size: 22),
                const SizedBox(height: SsSpace.s1),
                Text(label, style: TextStyle(fontFamily: 'Inter', fontSize: 13, fontWeight: FontWeight.w700, color: onTap == null ? SsColors.slate : color)),
              ]),
            ),
          ),
        ),
      );
}

/// Shared layout for the three forms: a scrolling form with a sticky Save button.
class _FormScaffold extends StatelessWidget {
  const _FormScaffold({required this.title, required this.form, required this.children, required this.busy, required this.saveLabel, required this.onSave});
  final String title;
  final GlobalKey<FormState> form;
  final List<Widget> children;
  final bool busy;
  final String saveLabel;
  final VoidCallback onSave;

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(title: Text(title), leading: IconButton(tooltip: 'Cancel', icon: const Icon(LucideIcons.x), onPressed: busy ? null : () => Navigator.pop(context))),
        body: Form(
          key: form,
          child: ListView(padding: const EdgeInsets.fromLTRB(SsSpace.s4, SsSpace.s2, SsSpace.s4, SsSpace.s6), children: children),
        ),
        bottomNavigationBar: BottomBarSafeArea(
          child: Padding(
            padding: const EdgeInsets.all(SsSpace.s4),
            child: FilledButton(
              style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(52)),
              onPressed: busy ? null : onSave,
              child: busy
                  ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2.4, color: SsColors.white))
                  : Text(saveLabel),
            ),
          ),
        ),
      );
}

String? _required(String? v, String what) => (v == null || v.trim().isEmpty) ? 'Enter $what.' : null;

// ---------------------------------------------------------------------------------------
// Note

class AddNoteScreen extends StatefulWidget {
  const AddNoteScreen({super.key, required this.session});
  final SessionDetail session;

  @override
  State<AddNoteScreen> createState() => _AddNoteScreenState();
}

class _AddNoteScreenState extends State<AddNoteScreen> {
  final _form = GlobalKey<FormState>();
  final _title = TextEditingController();
  final _body = TextEditingController();
  bool _publish = true;
  bool _busy = false;

  @override
  void dispose() {
    _title.dispose();
    _body.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    if (!(_form.currentState?.validate() ?? false)) return;
    setState(() => _busy = true);
    try {
      await AppScope.read(context).sessions.addNote(widget.session.id, title: _title.text.trim(), bodyMarkdown: _body.text.trim(), publish: _publish);
      if (!mounted) return;
      showSnack(context, _publish ? 'Note published to the class.' : 'Note saved as a draft.');
      Navigator.pop(context, true);
    } on ApiException catch (e) {
      if (mounted) showSnack(context, e.fieldError('title') ?? e.fieldError('bodyMarkdown') ?? e.message, error: true);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => _FormScaffold(
        title: 'Add a note',
        form: _form,
        busy: _busy,
        saveLabel: _publish ? 'Publish note' : 'Save draft',
        onSave: _save,
        children: [
          Text(widget.session.title, style: Theme.of(context).textTheme.bodySmall),
          const SizedBox(height: SsSpace.s3),
          TextFormField(
            controller: _title,
            enabled: !_busy,
            maxLength: 200,
            textCapitalization: TextCapitalization.sentences,
            decoration: const InputDecoration(labelText: 'Title *', hintText: 'e.g. Key points: quadratic formula'),
            validator: (v) => _required(v, 'a title'),
          ),
          const SizedBox(height: SsSpace.s2),
          TextFormField(
            controller: _body,
            enabled: !_busy,
            minLines: 8,
            maxLines: 20,
            textCapitalization: TextCapitalization.sentences,
            decoration: const InputDecoration(
              labelText: 'Note *',
              alignLabelWithHint: true,
              helperText: 'Plain text is fine. Use **bold**, - lists and # headings for formatting.',
            ),
            validator: (v) => _required(v, 'the note'),
          ),
          SwitchListTile(
            contentPadding: EdgeInsets.zero,
            value: _publish,
            onChanged: _busy ? null : (v) => setState(() => _publish = v),
            title: const Text('Publish to students now'),
            subtitle: const Text('Off: saved as a draft that only you can see.'),
          ),
        ],
      );
}

// ---------------------------------------------------------------------------------------
// Assignment

class AddAssignmentScreen extends StatefulWidget {
  const AddAssignmentScreen({super.key, required this.session});
  final SessionDetail session;

  @override
  State<AddAssignmentScreen> createState() => _AddAssignmentScreenState();
}

class _AddAssignmentScreenState extends State<AddAssignmentScreen> {
  final _form = GlobalKey<FormState>();
  final _title = TextEditingController();
  final _instructions = TextEditingController();
  final _marks = TextEditingController(text: '10');
  late DateTime _dueLocal;
  bool _allowLate = true;
  bool _publish = true;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    // Default: two days after the class, end of the day (in the teacher's zone).
    final time = AppScope.read(context).time;
    final day = time.viewerDate(widget.session.endUtc).add(const Duration(days: 2));
    _dueLocal = DateTime(day.year, day.month, day.day, 23, 59);
  }

  @override
  void dispose() {
    _title.dispose();
    _instructions.dispose();
    _marks.dispose();
    super.dispose();
  }

  DateTime _dueUtc() {
    final time = AppScope.read(context).time;
    final midnight = time.startOfViewerDayUtc(DateTime.utc(_dueLocal.year, _dueLocal.month, _dueLocal.day));
    return midnight.add(Duration(hours: _dueLocal.hour, minutes: _dueLocal.minute));
  }

  Future<void> _pickDue() async {
    final date = await showDatePicker(
      context: context,
      initialDate: _dueLocal,
      firstDate: DateTime.now().subtract(const Duration(days: 1)),
      lastDate: DateTime.now().add(const Duration(days: 365)),
    );
    if (date == null || !mounted) return;
    final t = await showTimePicker(context: context, initialTime: TimeOfDay.fromDateTime(_dueLocal));
    if (!mounted) return;
    setState(() => _dueLocal = DateTime(date.year, date.month, date.day, t?.hour ?? _dueLocal.hour, t?.minute ?? _dueLocal.minute));
  }

  Future<void> _save() async {
    if (!(_form.currentState?.validate() ?? false)) return;
    final due = _dueUtc();
    if (!due.isAfter(DateTime.now().toUtc())) {
      showSnack(context, 'Choose a due date in the future.', error: true);
      return;
    }
    setState(() => _busy = true);
    try {
      await AppScope.read(context).learning.createAssignment(
            sessionId: widget.session.id,
            title: _title.text.trim(),
            instructionsMarkdown: _instructions.text.trim(),
            dueAtUtc: due,
            maxMarks: double.parse(_marks.text.trim().replaceAll(',', '.')),
            allowLateSubmission: _allowLate,
            publish: _publish,
          );
      if (!mounted) return;
      showSnack(context, _publish ? 'Assignment published to the class.' : 'Assignment saved as a draft.');
      Navigator.pop(context, true);
    } on ApiException catch (e) {
      if (mounted) {
        showSnack(context, e.fieldError('title') ?? e.fieldError('dueAtUtc') ?? e.fieldError('maxMarks') ?? e.fieldError('studentIds') ?? e.message, error: true);
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final time = AppScope.of(context).time;
    return _FormScaffold(
      title: 'New assignment',
      form: _form,
      busy: _busy,
      saveLabel: _publish ? 'Publish assignment' : 'Save draft',
      onSave: _save,
      children: [
        Text('For the students of ${widget.session.title}', style: Theme.of(context).textTheme.bodySmall),
        const SizedBox(height: SsSpace.s3),
        TextFormField(
          controller: _title,
          enabled: !_busy,
          maxLength: 200,
          textCapitalization: TextCapitalization.sentences,
          decoration: const InputDecoration(labelText: 'Title *', hintText: 'e.g. Worksheet 5: discriminant'),
          validator: (v) => _required(v, 'a title'),
        ),
        const SizedBox(height: SsSpace.s2),
        TextFormField(
          controller: _instructions,
          enabled: !_busy,
          minLines: 5,
          maxLines: 14,
          textCapitalization: TextCapitalization.sentences,
          decoration: const InputDecoration(labelText: 'Instructions *', alignLabelWithHint: true),
          validator: (v) => _required(v, 'the instructions'),
        ),
        const SizedBox(height: SsSpace.s4),
        SsListGroup(children: [
          SsListRow(
            icon: LucideIcons.calendarClock,
            title: 'Due',
            subtitle: '${DateFormat('EEE d MMM y · HH:mm').format(_dueLocal)} (${time.viewerZoneId})',
            onTap: _busy ? null : _pickDue,
          ),
        ]),
        const SizedBox(height: SsSpace.s4),
        TextFormField(
          controller: _marks,
          enabled: !_busy,
          keyboardType: const TextInputType.numberWithOptions(decimal: true),
          inputFormatters: [FilteringTextInputFormatter.allow(RegExp(r'[0-9.,]'))],
          decoration: const InputDecoration(labelText: 'Maximum marks *'),
          validator: (v) {
            final n = double.tryParse((v ?? '').trim().replaceAll(',', '.'));
            return n == null || n <= 0 || n > 1000 ? 'Enter marks between 1 and 1000.' : null;
          },
        ),
        SwitchListTile(
          contentPadding: EdgeInsets.zero,
          value: _allowLate,
          onChanged: _busy ? null : (v) => setState(() => _allowLate = v),
          title: const Text('Accept late submissions'),
        ),
        SwitchListTile(
          contentPadding: EdgeInsets.zero,
          value: _publish,
          onChanged: _busy ? null : (v) => setState(() => _publish = v),
          title: const Text('Publish to students now'),
          subtitle: const Text('Off: saved as a draft that only you can see.'),
        ),
      ],
    );
  }
}

// ---------------------------------------------------------------------------------------
// Video

class UploadVideoScreen extends StatefulWidget {
  const UploadVideoScreen({super.key, required this.session});
  final SessionDetail session;

  @override
  State<UploadVideoScreen> createState() => _UploadVideoScreenState();
}

class _UploadVideoScreenState extends State<UploadVideoScreen> {
  static const _maxBytes = 2000 * 1000 * 1000;
  static const _offsets = [0, 30, 60, 1440];

  final _form = GlobalKey<FormState>();
  late final _title = TextEditingController(text: '${widget.session.title} — recording');
  final _description = TextEditingController();
  XFile? _file;
  int? _size;
  VideoUnlockRule _rule = VideoUnlockRule.afterClass;
  int _offset = 0;
  bool _busy = false;

  @override
  void dispose() {
    _title.dispose();
    _description.dispose();
    super.dispose();
  }

  Future<void> _pick() async {
    try {
      final file = await ImagePicker().pickVideo(source: ImageSource.gallery);
      if (file == null || !mounted) return;
      final size = await File(file.path).length();
      setState(() {
        _file = file;
        _size = size;
      });
    } on PlatformException {
      if (mounted) showSnack(context, "Couldn't open your videos. Allow photo access in Settings.", error: true);
    }
  }

  static String _contentType(String name) {
    final n = name.toLowerCase();
    if (n.endsWith('.webm')) return 'video/webm';
    if (n.endsWith('.mov')) return 'video/quicktime';
    return 'video/mp4';
  }

  Future<void> _upload() async {
    final file = _file;
    if (file == null) {
      showSnack(context, 'Choose a video first.', error: true);
      return;
    }
    if (!(_form.currentState?.validate() ?? false)) return;
    if ((_size ?? 0) > _maxBytes) {
      showSnack(context, 'That video is larger than 2 GB.', error: true);
      return;
    }
    setState(() => _busy = true);
    try {
      await AppScope.read(context).sessions.uploadVideo(
            widget.session.id,
            filePath: file.path,
            filename: file.name,
            contentType: _contentType(file.name),
            title: _title.text.trim(),
            description: _description.text.trim(),
            rule: _rule,
            offsetMinutes: _offset,
          );
      if (!mounted) return;
      showSnack(context, 'Video uploaded. Students can watch it when it unlocks.');
      Navigator.pop(context, true);
    } on ApiException catch (e) {
      if (mounted) showSnack(context, e.fieldError('file') ?? e.fieldError('title') ?? e.message, error: true);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  String _offsetLabel(int minutes) =>
      minutes == 0 ? (_rule == VideoUnlockRule.afterClass ? 'When the class ends' : 'When the class starts') : unlockOffsetLabel(minutes);

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).textTheme;
    final file = _file;
    return _FormScaffold(
      title: 'Upload a video',
      form: _form,
      busy: _busy,
      saveLabel: _busy ? 'Uploading…' : 'Upload video',
      onSave: _upload,
      children: [
        Text('Optional — for a class recording or a pre-recorded lesson.', style: t.bodySmall),
        const SizedBox(height: SsSpace.s3),
        SsCard(
          onTap: _busy ? null : _pick,
          child: Row(children: [
            IconTile(file == null ? LucideIcons.videoOff : LucideIcons.fileVideo, color: SsColors.navy, size: 48),
            const SizedBox(width: SsSpace.s3),
            Expanded(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(file == null ? 'Choose a video' : file.name, style: t.titleSmall, maxLines: 1, overflow: TextOverflow.ellipsis),
                Text(file == null ? 'MP4, MOV or WebM, up to 2 GB' : '${((_size ?? 0) / 1e6).toStringAsFixed(1)} MB · tap to change', style: t.bodySmall),
              ]),
            ),
            const Icon(LucideIcons.chevronRight, size: 18, color: SsColors.slate),
          ]),
        ),
        const SizedBox(height: SsSpace.s4),
        TextFormField(
          controller: _title,
          enabled: !_busy,
          maxLength: 200,
          decoration: const InputDecoration(labelText: 'Title *'),
          validator: (v) => _required(v, 'a title'),
        ),
        TextFormField(
          controller: _description,
          enabled: !_busy,
          minLines: 2,
          maxLines: 5,
          decoration: const InputDecoration(labelText: 'Description (optional)', alignLabelWithHint: true),
        ),
        const SizedBox(height: SsSpace.s4),
        Text('When can students watch it?', style: t.titleSmall),
        const SizedBox(height: SsSpace.s2),
        SsSegmented<VideoUnlockRule>(
          segments: const [
            (value: VideoUnlockRule.afterClass, label: 'After the class', icon: null),
            (value: VideoUnlockRule.beforeClass, label: 'Before the class', icon: null),
          ],
          selected: _rule,
          onChanged: (v) => setState(() => _rule = v),
        ),
        const SizedBox(height: SsSpace.s3),
        Wrap(spacing: SsSpace.s2, runSpacing: SsSpace.s2, children: [
          for (final m in _offsets)
            ChoiceChip(
              label: Text(_offsetLabel(m)),
              selected: _offset == m,
              showCheckmark: false,
              onSelected: _busy ? null : (_) => setState(() => _offset = m),
            ),
        ]),
        if (_busy) ...[
          const SizedBox(height: SsSpace.s4),
          const LinearProgressIndicator(),
          const SizedBox(height: SsSpace.s2),
          Text('Uploading… keep the app open until it finishes.', style: t.bodySmall),
        ],
      ],
    );
  }
}
