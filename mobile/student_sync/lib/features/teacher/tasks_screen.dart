import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/api/api_exception.dart';
import '../../core/app_services.dart';
import '../../core/models/dashboard_models.dart';
import '../../core/models/enums.dart';
import '../../core/models/learning_models.dart';
import '../../core/theme/status_styles.dart';
import '../../core/theme/tokens.dart';
import '../../core/time/time_format.dart';
import '../../core/widgets/async_view.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/illustration.dart';
import '../../core/widgets/notification_bell.dart';
import '../../core/widgets/states.dart';
import '../doubts/doubts_list_screen.dart';

/// Teacher "Tasks" on one page: submissions to grade, attendance to mark, then student
/// doubts to answer (and those already answered or closed).
/// `/teacher/tasks?tab=doubts` scrolls to the doubts.
class TasksScreen extends StatefulWidget {
  const TasksScreen({super.key});

  @override
  State<TasksScreen> createState() => _TasksScreenState();
}

class _TasksScreenState extends State<TasksScreen> {
  final _doubtsAnchor = GlobalKey();
  int _version = 0;
  String? _lastTabParam;
  bool _scrollToDoubts = false;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    // Follow `?tab=doubts` each time the route changes (e.g. "Doubts" on the dashboard).
    final param = GoRouterState.of(context).uri.queryParameters['tab'];
    if (param != _lastTabParam) {
      _lastTabParam = param;
      if (param == 'doubts') {
        _scrollToDoubts = true;
        WidgetsBinding.instance.addPostFrameCallback((_) => _maybeScrollToDoubts());
      }
    }
  }

  void _maybeScrollToDoubts() {
    final target = _doubtsAnchor.currentContext;
    if (!_scrollToDoubts || target == null || !mounted) return;
    _scrollToDoubts = false;
    Scrollable.ensureVisible(target, duration: SsMotion.medium, curve: SsMotion.curve);
  }

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    return Scaffold(
      appBar: AppBar(title: const Text('Tasks'), actions: tabBarActions),
      body: RefreshIndicator(
        onRefresh: () async => setState(() => _version++),
        child: ListView(padding: pagePadding(context), children: [
          AsyncSection<TeacherDashboard>(
            key: ValueKey('todo/$_version'),
            load: services.dashboards.teacher,
            builder: (context, d, reload) => _ToDo(d: d, reload: reload),
          ),
          KeyedSubtree(
            key: _doubtsAnchor,
            child: AsyncSection(
              key: ValueKey('doubts/$_version'),
              load: () => services.doubts.list(unresolvedOnly: false),
              onLoaded: _maybeScrollToDoubts,
              builder: (context, page, reload) => DoubtGroups(items: page.items, showStudent: true, onReturn: reload, openTitle: 'Doubts to answer'),
            ),
          ),
        ]),
      ),
    );
  }
}

class _ToDo extends StatelessWidget {
  const _ToDo({required this.d, required this.reload});
  final TeacherDashboard d;
  final Future<void> Function() reload;

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    final t = Theme.of(context).textTheme;
    return Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
      SectionHeader('To grade', icon: LucideIcons.award, iconColor: SsColors.success, count: d.submissionsToGrade.length),
      if (d.submissionsToGrade.isEmpty) const InlineEmpty(icon: LucideIcons.circleCheckBig, text: 'Nothing waiting to be graded.'),
      for (final s in d.submissionsToGrade) ...[SubmissionToGradeCard(item: s, onReturn: reload), const SizedBox(height: SsSpace.s3)],
      SectionHeader('Attendance to mark', icon: LucideIcons.clipboardCheck, iconColor: SsColors.warning, count: d.pendingAttendance.length),
      if (d.pendingAttendance.isEmpty) const InlineEmpty(icon: LucideIcons.clipboardCheck, text: 'All attendance is up to date.'),
      for (final s in d.pendingAttendance) ...[
        SsCard(
          onTap: () async {
            await context.push('/sessions/${s.id}?tab=attendance');
            await reload();
          },
          child: Row(children: [
            const IconTile(LucideIcons.clipboardCheck, color: SsColors.warning),
            const SizedBox(width: SsSpace.s3),
            Expanded(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(s.title, style: t.titleSmall),
                Text(services.time.range(s.startUtc, s.endUtc), style: t.bodySmall),
              ]),
            ),
            const ExcludeSemantics(child: Icon(LucideIcons.chevronRight, size: 18, color: SsColors.slate)),
          ]),
        ),
        const SizedBox(height: SsSpace.s3),
      ],
    ]);
  }
}

class SubmissionToGradeCard extends StatelessWidget {
  const SubmissionToGradeCard({super.key, required this.item, required this.onReturn});
  final SubmissionToGrade item;
  final Future<void> Function() onReturn;

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).textTheme;
    final time = AppScope.of(context).time;
    return SsCard(
      onTap: () async {
        await context.push('/grading/${item.assignmentId}/${item.submissionId}');
        await onReturn();
      },
      child: Row(children: [
        const IconTile(LucideIcons.award, color: SsColors.success),
        const SizedBox(width: SsSpace.s3),
        Expanded(
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text(item.assignmentTitle, style: t.titleSmall),
            Text(
              [item.studentName, if (item.submittedAtUtc != null) 'submitted ${time.dateTime(item.submittedAtUtc!)}'].join(' · '),
              style: t.bodySmall,
            ),
          ]),
        ),
        if (item.isLate) const Padding(padding: EdgeInsets.only(right: SsSpace.s2), child: StatusBadge(StatusStyle('Late', SsColors.warning), dense: true)),
        const ExcludeSemantics(child: Icon(LucideIcons.chevronRight, size: 18, color: SsColors.slate)),
      ]),
    );
  }
}

/// Grade one submission: marks (0..max), feedback, optional return for correction.
class GradeSubmissionScreen extends StatelessWidget {
  const GradeSubmissionScreen({super.key, required this.assignmentId, required this.submissionId});
  final String assignmentId;
  final String submissionId;

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    return Scaffold(
      appBar: AppBar(title: const Text('Grade submission')),
      body: AsyncView<Submission?>(
        load: () async => (await services.learning.submissions(assignmentId)).where((s) => s.id == submissionId).firstOrNull,
        isEmpty: (s) => s == null,
        empty: (context, _) => const EmptyState(illustration: Illustration.notFound, title: 'Submission not found', message: 'It may have been withdrawn or you no longer teach this assignment.'),
        builder: (context, s, reload) => GradeForm(submission: s!, onSaved: () => context.pop()),
      ),
    );
  }
}

class GradeForm extends StatefulWidget {
  const GradeForm({super.key, required this.submission, required this.onSaved});
  final Submission submission;
  final VoidCallback onSaved;

  @override
  State<GradeForm> createState() => _GradeFormState();
}

class _GradeFormState extends State<GradeForm> {
  final _form = GlobalKey<FormState>();
  late final _marks = TextEditingController(text: widget.submission.marks == null ? '' : formatMarks(widget.submission.marks!));
  late final _feedback = TextEditingController(text: widget.submission.feedback ?? '');
  bool _returnForCorrection = false;
  bool _busy = false;
  String? _serverMarksError;

  @override
  void dispose() {
    _marks.dispose();
    _feedback.dispose();
    super.dispose();
  }

  String? _validateMarks(String? v) {
    if (_serverMarksError != null) return _serverMarksError;
    final n = double.tryParse((v ?? '').trim().replaceAll(',', '.'));
    if (n == null) return 'Enter the marks as a number.';
    if (n < 0 || n > widget.submission.maxMarks) return 'Marks must be between 0 and ${formatMarks(widget.submission.maxMarks)}.';
    return null;
  }

  Future<void> _save() async {
    setState(() => _serverMarksError = null);
    if (!(_form.currentState?.validate() ?? false)) return;
    setState(() => _busy = true);
    try {
      await AppScope.read(context).learning.grade(
            widget.submission.id,
            marks: double.parse(_marks.text.trim().replaceAll(',', '.')),
            feedback: _feedback.text,
            returnForCorrection: _returnForCorrection,
            rowVersion: widget.submission.rowVersion,
          );
      if (!mounted) return;
      showSnack(context, _returnForCorrection ? 'Returned to ${widget.submission.studentName} for correction.' : 'Grade saved for ${widget.submission.studentName}.');
      widget.onSaved();
    } on ApiException catch (e) {
      if (!mounted) return;
      if (e.fieldError('marks') != null) {
        setState(() => _serverMarksError = e.fieldError('marks'));
        _form.currentState?.validate();
      } else {
        showSnack(context, e.isConflict ? 'Someone else changed this submission. Go back and open it again.' : e.message, error: true);
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final s = widget.submission;
    final t = Theme.of(context).textTheme;
    final time = AppScope.of(context).time;
    return Form(
      key: _form,
      child: ListView(padding: pagePadding(context), children: [
        const SizedBox(height: SsSpace.s2),
        Semantics(header: true, child: Text(s.assignmentTitle, style: t.headlineSmall)),
        const SizedBox(height: SsSpace.s1),
        Text(s.studentName, style: t.titleSmall),
        const SizedBox(height: SsSpace.s2),
        Wrap(spacing: SsSpace.s2, children: [
          StatusBadge(StatusStyles.submission(s.status)),
          if (s.isLate) const StatusBadge(StatusStyle('Late', SsColors.warning)),
        ]),
        if (s.submittedAtUtc != null) ...[
          const SizedBox(height: SsSpace.s1),
          Text('Submitted ${time.dateTime(s.submittedAtUtc!)} · attempt ${s.attemptNumber}', style: t.bodySmall),
        ],
        const SectionHeader('Answer'),
        SsCard(child: SelectableText((s.textAnswer?.isNotEmpty ?? false) ? s.textAnswer! : 'No written answer.', style: t.bodyMedium)),
        if (s.files.isNotEmpty) ...[
          const SizedBox(height: SsSpace.s2),
          Text('${s.files.length} attached file(s) — review them in the web app.', style: t.bodySmall),
        ],
        const SectionHeader('Grade'),
        TextFormField(
          controller: _marks,
          enabled: !_busy,
          keyboardType: const TextInputType.numberWithOptions(decimal: true),
          decoration: InputDecoration(labelText: 'Marks *', suffixText: '/ ${formatMarks(s.maxMarks)}'),
          validator: _validateMarks,
        ),
        const SizedBox(height: SsSpace.s3),
        TextFormField(
          controller: _feedback,
          enabled: !_busy,
          minLines: 3,
          maxLines: 8,
          maxLength: 4000,
          decoration: const InputDecoration(labelText: 'Feedback for the student', alignLabelWithHint: true),
        ),
        if (s.status != SubmissionStatus.returnedForCorrection)
          SwitchListTile(
            contentPadding: EdgeInsets.zero,
            value: _returnForCorrection,
            onChanged: _busy ? null : (v) => setState(() => _returnForCorrection = v),
            title: const Text('Return for correction'),
            subtitle: const Text('The student can revise and resubmit.'),
          ),
        const SizedBox(height: SsSpace.s4),
        FilledButton(
          onPressed: _busy ? null : _save,
          child: _busy
              ? const SizedBox(width: 20, height: 20, child: CircularProgressIndicator(strokeWidth: 2, color: SsColors.white))
              : Text(_returnForCorrection ? 'Return to student' : 'Save grade'),
        ),
      ]),
    );
  }
}
