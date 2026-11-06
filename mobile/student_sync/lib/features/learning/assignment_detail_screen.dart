import 'package:flutter/material.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/api/api_client.dart';
import '../../core/api/api_exception.dart';
import '../../core/app_services.dart';
import '../../core/models/enums.dart';
import '../../core/models/learning_models.dart';
import '../../core/theme/status_styles.dart';
import '../../core/theme/tokens.dart';
import '../../core/time/time_format.dart';
import '../../core/widgets/async_view.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/states.dart';

class _AssignmentData {
  const _AssignmentData(this.assignment, this.submission);
  final AssignmentDetail assignment;
  final Submission? submission;
}

/// Whether the student can still edit their answer, and why not when they can't.
({bool editable, String? reason}) submissionEditability(AssignmentDetail a, Submission? s, DateTime nowUtc) {
  if (a.status == AssignmentStatus.closed) return (editable: false, reason: 'This assignment is closed.');
  final status = s?.status;
  if (status == SubmissionStatus.graded) return (editable: false, reason: 'Your submission has been graded.');
  if (status == SubmissionStatus.submitted && !a.allowResubmission) {
    return (editable: false, reason: 'Submitted. Resubmission is not allowed for this assignment.');
  }
  if (nowUtc.isAfter(a.dueAtUtc) && !a.allowLateSubmission && status != SubmissionStatus.returnedForCorrection) {
    return (editable: false, reason: 'The due date has passed and late submissions are not accepted.');
  }
  return (editable: true, reason: null);
}

class AssignmentDetailScreen extends StatelessWidget {
  const AssignmentDetailScreen({super.key, required this.assignmentId});
  final String assignmentId;

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    return Scaffold(
      appBar: AppBar(title: const Text('Assignment')),
      body: AsyncView<_AssignmentData>(
        load: () async {
          final results = await Future.wait([
            services.learning.assignment(assignmentId),
            services.learning.mySubmission(assignmentId),
          ]);
          return _AssignmentData(results[0]! as AssignmentDetail, results[1] as Submission?);
        },
        builder: (context, data, reload) => _AssignmentBody(data: data, reload: reload),
      ),
    );
  }
}

class _AssignmentBody extends StatefulWidget {
  const _AssignmentBody({required this.data, required this.reload});
  final _AssignmentData data;
  final Future<void> Function() reload;

  @override
  State<_AssignmentBody> createState() => _AssignmentBodyState();
}

class _AssignmentBodyState extends State<_AssignmentBody> {
  late final TextEditingController _answer = TextEditingController(text: widget.data.submission?.textAnswer ?? '');
  bool _saving = false;
  String? _fieldError;

  @override
  void didUpdateWidget(covariant _AssignmentBody oldWidget) {
    super.didUpdateWidget(oldWidget);
    final text = widget.data.submission?.textAnswer ?? '';
    if (oldWidget.data.submission?.rowVersion != widget.data.submission?.rowVersion && _answer.text != text) _answer.text = text;
  }

  @override
  void dispose() {
    _answer.dispose();
    super.dispose();
  }

  Future<void> _save({required bool submit}) async {
    final text = _answer.text.trim();
    if (submit && text.isEmpty) {
      setState(() => _fieldError = 'Write your answer before submitting.');
      return;
    }
    if (submit) {
      final ok = await showDialog<bool>(
        context: context,
        builder: (context) => AlertDialog(
          title: const Text('Submit your answer?'),
          content: Text(widget.data.assignment.allowResubmission
              ? 'Your teacher will be able to see and grade it. You can resubmit before it is graded.'
              : 'Your teacher will be able to see and grade it. You cannot change it after submitting.'),
          actions: [
            TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Keep editing')),
            FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Submit')),
          ],
        ),
      );
      if (ok != true || !mounted) return;
    }
    setState(() {
      _saving = true;
      _fieldError = null;
    });
    try {
      await AppScope.read(context).learning.saveSubmission(
            widget.data.assignment.id,
            textAnswer: text,
            submit: submit,
            rowVersion: widget.data.submission?.rowVersion,
            idempotencyKey: newIdempotencyKey(),
          );
      if (!mounted) return;
      showSnack(context, submit ? 'Answer submitted.' : 'Draft saved. It is not visible to your teacher until you submit.');
      await widget.reload();
    } on ApiException catch (e) {
      if (!mounted) return;
      final field = e.fieldError('textAnswer');
      if (field != null) {
        setState(() => _fieldError = field);
      } else {
        showSnack(context, e.isConflict ? 'This submission changed elsewhere. Pull down to reload, then try again.' : e.message, error: true);
      }
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final a = widget.data.assignment;
    final s = widget.data.submission;
    final t = Theme.of(context).textTheme;
    final time = AppScope.of(context).time;
    final now = DateTime.now().toUtc();
    final edit = submissionEditability(a, s, now);
    final overdue = now.isAfter(a.dueAtUtc);

    return RefreshIndicator(
      onRefresh: widget.reload,
      child: ListView(padding: pagePadding(context), children: [
        const SizedBox(height: SsSpace.s2),
        Semantics(header: true, child: Text(a.title, style: t.headlineSmall)),
        const SizedBox(height: SsSpace.s2),
        Wrap(spacing: SsSpace.s2, runSpacing: SsSpace.s2, children: [
          StatusBadge(StatusStyles.submission(s?.status)),
          if (s?.isLate ?? false) const StatusBadge(StatusStyle('Late', SsColors.warning)),
        ]),
        const SizedBox(height: SsSpace.s3),
        SsCard(
          child: Column(children: [
            InfoRow(
              icon: LucideIcons.calendar,
              label: 'Due',
              value: '${time.dateTime(a.dueAtUtc)} (${time.viewerZoneId})${overdue ? ' — past due' : ''}',
            ),
            InfoRow(icon: LucideIcons.award, label: 'Marks', value: formatMarks(a.maxMarks)),
            InfoRow(icon: LucideIcons.user, label: 'Teacher', value: a.teacherName),
            if (a.sessionTitle != null) InfoRow(icon: LucideIcons.presentation, label: 'Class', value: a.sessionTitle!),
            InfoRow(
              icon: LucideIcons.listChecks,
              label: 'Rules',
              value: [
                a.allowLateSubmission ? 'Late submissions accepted' : 'No late submissions',
                a.allowResubmission ? 'resubmission allowed' : 'one submission only',
              ].join(' · '),
            ),
          ]),
        ),
        const SectionHeader('Instructions'),
        SsCard(child: MarkdownView(a.instructionsMarkdown)),
        if (a.attachments.isNotEmpty) ...[
          const SectionHeader('Attachments'),
          for (final f in a.attachments)
            _AttachmentTile(fileId: f.id, name: f.fileName),
        ],
        if (s?.status == SubmissionStatus.graded || s?.feedback != null) ...[
          const SectionHeader('Feedback'),
          SsCard(
            color: SsColors.blue50,
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              if (s!.marks != null) Text('${formatMarks(s.marks!)} / ${formatMarks(s.maxMarks)}', style: t.headlineSmall),
              if (s.gradedAtUtc != null) Text('Graded ${time.dateTime(s.gradedAtUtc!)}', style: t.bodySmall),
              if (s.feedback != null) ...[const SizedBox(height: SsSpace.s2), Text(s.feedback!, style: t.bodyMedium)],
            ]),
          ),
        ],
        const SectionHeader('Your answer'),
        if (s?.submittedAtUtc != null)
          Padding(
            padding: const EdgeInsets.only(bottom: SsSpace.s2),
            child: Text('Submitted ${time.dateTime(s!.submittedAtUtc!)} · attempt ${s.attemptNumber}', style: t.bodySmall),
          ),
        if (edit.editable) ...[
          TextField(
            controller: _answer,
            minLines: 6,
            maxLines: 16,
            maxLength: 20000,
            enabled: !_saving,
            decoration: InputDecoration(labelText: 'Answer', alignLabelWithHint: true, errorText: _fieldError),
            onChanged: (_) {
              if (_fieldError != null) setState(() => _fieldError = null);
            },
          ),
          const SizedBox(height: SsSpace.s2),
          Text('File attachments can be added from the web app.', style: t.bodySmall),
          const SizedBox(height: SsSpace.s3),
          Row(children: [
            Expanded(
              child: OutlinedButton(onPressed: _saving ? null : () => _save(submit: false), child: const Text('Save draft')),
            ),
            const SizedBox(width: SsSpace.s3),
            Expanded(
              child: FilledButton(
                onPressed: _saving ? null : () => _save(submit: true),
                child: _saving
                    ? const SizedBox(width: 20, height: 20, child: CircularProgressIndicator(strokeWidth: 2, color: SsColors.white))
                    : Text(s?.status == SubmissionStatus.submitted ? 'Resubmit' : 'Submit'),
              ),
            ),
          ]),
        ] else ...[
          SsCard(
            child: Text((s?.textAnswer?.isNotEmpty ?? false) ? s!.textAnswer! : 'No written answer.', style: t.bodyMedium),
          ),
          if (edit.reason != null) ...[
            const SizedBox(height: SsSpace.s2),
            Text(edit.reason!, style: t.bodySmall),
          ],
        ],
      ]),
    );
  }
}

class _AttachmentTile extends StatefulWidget {
  const _AttachmentTile({required this.fileId, required this.name});
  final String fileId;
  final String name;

  @override
  State<_AttachmentTile> createState() => _AttachmentTileState();
}

class _AttachmentTileState extends State<_AttachmentTile> {
  bool _busy = false;

  Future<void> _open() async {
    setState(() => _busy = true);
    try {
      final services = AppScope.read(context);
      final link = await services.sessions.fileLink(widget.fileId);
      if (mounted) await openExternal(context, services.config.resolveServerPath(link.url).toString(), inApp: true);
    } on Object catch (e) {
      if (mounted) showSnack(context, describeError(e), error: true);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(bottom: SsSpace.s2),
        child: SsCard(
          padding: EdgeInsets.zero,
          child: ListTile(
            leading: const Icon(LucideIcons.paperclip),
            title: Text(widget.name),
            trailing: _busy ? const SizedBox(width: 20, height: 20, child: CircularProgressIndicator(strokeWidth: 2)) : const Icon(LucideIcons.externalLink),
            onTap: _busy ? null : _open,
          ),
        ),
      );
}
