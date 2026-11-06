import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_client.dart';
import '../../core/api/api_exception.dart';
import '../../core/app_services.dart';
import '../../core/models/doubt_models.dart';
import '../../core/theme/tokens.dart';
import '../../core/widgets/async_view.dart';
import '../../core/widgets/states.dart';

class NewDoubtScreen extends StatelessWidget {
  const NewDoubtScreen({super.key, this.sessionId});
  final String? sessionId;

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    return Scaffold(
      appBar: AppBar(title: const Text('Ask a doubt')),
      body: AsyncView<List<Subject>>(
        load: services.doubts.subjects,
        builder: (context, subjects, _) => NewDoubtForm(subjects: subjects, sessionId: sessionId),
      ),
    );
  }
}

class NewDoubtForm extends StatefulWidget {
  const NewDoubtForm({super.key, required this.subjects, this.sessionId});
  final List<Subject> subjects;
  final String? sessionId;

  @override
  State<NewDoubtForm> createState() => _NewDoubtFormState();
}

class _NewDoubtFormState extends State<NewDoubtForm> {
  final _form = GlobalKey<FormState>();
  final _title = TextEditingController();
  final _description = TextEditingController();
  String? _subjectId;
  String? _chapterId;
  bool _private = true;
  bool _busy = false;
  Map<String, List<String>> _serverErrors = const {};

  /// One key per form submission, so a retried request is not created twice.
  String _idempotencyKey = newIdempotencyKey();

  @override
  void dispose() {
    _title.dispose();
    _description.dispose();
    super.dispose();
  }

  String? _server(String field) {
    for (final e in _serverErrors.entries) {
      if (e.key.toLowerCase() == field.toLowerCase() && e.value.isNotEmpty) return e.value.first;
    }
    return null;
  }

  Future<void> _submit() async {
    setState(() => _serverErrors = const {});
    if (!(_form.currentState?.validate() ?? false)) return;
    setState(() => _busy = true);
    try {
      final id = await AppScope.read(context).doubts.create(
            title: _title.text.trim(),
            description: _description.text.trim(),
            subjectId: _subjectId,
            chapterId: _chapterId,
            sessionId: widget.sessionId,
            isPrivate: _private,
            idempotencyKey: _idempotencyKey,
          );
      if (!mounted) return;
      showSnack(context, 'Doubt sent to your teacher.');
      context.pop(id);
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() => _serverErrors = e.fieldErrors);
      _form.currentState?.validate();
      if (e.fieldErrors.isEmpty) showSnack(context, e.message, error: true);
      // A definitive rejection means the next attempt is a new request.
      if (!e.isNetwork) _idempotencyKey = newIdempotencyKey();
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).textTheme;
    final subject = widget.subjects.where((s) => s.id == _subjectId).firstOrNull;
    return Form(
      key: _form,
      child: ListView(padding: const EdgeInsets.all(SsSpace.s4), children: [
        Text('Your teacher will reply here. Add as much detail as you can.', style: t.bodyMedium),
        const SizedBox(height: SsSpace.s4),
        TextFormField(
          controller: _title,
          enabled: !_busy,
          maxLength: 200,
          decoration: const InputDecoration(labelText: 'Question *', hintText: 'e.g. Why do we split the middle term?'),
          textInputAction: TextInputAction.next,
          validator: (v) {
            if (_server('title') != null) return _server('title');
            return (v?.trim().isEmpty ?? true) ? 'Write your question.' : null;
          },
        ),
        const SizedBox(height: SsSpace.s3),
        TextFormField(
          controller: _description,
          enabled: !_busy,
          minLines: 4,
          maxLines: 10,
          maxLength: 8000,
          decoration: const InputDecoration(labelText: 'Details *', hintText: 'What have you tried? Where are you stuck?', alignLabelWithHint: true),
          validator: (v) {
            if (_server('description') != null) return _server('description');
            return (v?.trim().isEmpty ?? true) ? 'Describe what you are stuck on.' : null;
          },
        ),
        const SizedBox(height: SsSpace.s3),
        DropdownButtonFormField<String>(
          initialValue: _subjectId,
          isExpanded: true,
          decoration: InputDecoration(labelText: 'Subject', errorText: _server('subjectId')),
          hint: const Text('Choose a subject (optional)'),
          items: [
            for (final s in widget.subjects) DropdownMenuItem(value: s.id, child: Text('${s.name} (${s.code})', overflow: TextOverflow.ellipsis)),
          ],
          onChanged: _busy ? null : (v) => setState(() {
                _subjectId = v;
                _chapterId = null;
              }),
        ),
        if (subject != null && subject.chapters.isNotEmpty) ...[
          const SizedBox(height: SsSpace.s3),
          DropdownButtonFormField<String>(
            key: ValueKey(subject.id),
            initialValue: _chapterId,
            isExpanded: true,
            decoration: const InputDecoration(labelText: 'Chapter'),
            hint: const Text('Choose a chapter (optional)'),
            items: [for (final c in subject.chapters) DropdownMenuItem(value: c.id, child: Text(c.name, overflow: TextOverflow.ellipsis))],
            onChanged: _busy ? null : (v) => setState(() => _chapterId = v),
          ),
        ],
        const SizedBox(height: SsSpace.s2),
        SwitchListTile(
          contentPadding: EdgeInsets.zero,
          value: _private,
          onChanged: _busy ? null : (v) => setState(() => _private = v),
          title: const Text('Keep private'),
          subtitle: const Text('Only you and your teachers can see this doubt.'),
        ),
        const SizedBox(height: SsSpace.s4),
        FilledButton(
          onPressed: _busy ? null : _submit,
          child: _busy
              ? const SizedBox(width: 20, height: 20, child: CircularProgressIndicator(strokeWidth: 2, color: SsColors.white))
              : const Text('Send to teacher'),
        ),
      ]),
    );
  }
}
