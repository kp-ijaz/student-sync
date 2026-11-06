import 'dart:async';

import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/api/api_exception.dart';
import '../../core/app_services.dart';
import '../../core/models/enums.dart';
import '../../core/models/learning_models.dart';
import '../../core/theme/tokens.dart';
import '../../core/time/time_format.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/states.dart';
import 'learning_api.dart';
import 'question_input.dart';

enum SaveState { idle, pending, saving, saved, failed }

/// Saves answers as the student works: choices immediately, typing after a short
/// pause. One save per question is in flight at a time; newer edits win.
class AnswerAutosaver {
  AnswerAutosaver({required this.attemptId, required LearningApi api, this.debounce = const Duration(milliseconds: 800), this.onStateChanged})
      : _api = api;

  final String attemptId;
  final LearningApi _api;
  final Duration debounce;
  final void Function()? onStateChanged;

  final Map<String, ExamResponse> _pending = {};
  final Map<String, Timer> _timers = {};
  final Map<String, Future<void>> _inFlight = {};
  final Map<String, SaveState> states = {};
  ApiException? lastError;

  bool get hasUnsaved => _pending.isNotEmpty || _inFlight.isNotEmpty || states.values.any((s) => s == SaveState.failed);

  void update(String questionId, ExamResponse response, {bool immediate = false}) {
    _pending[questionId] = response;
    _set(questionId, SaveState.pending);
    _timers.remove(questionId)?.cancel();
    if (immediate) {
      unawaited(_flushOne(questionId));
    } else {
      _timers[questionId] = Timer(debounce, () => unawaited(_flushOne(questionId)));
    }
  }

  Future<void> _flushOne(String questionId) async {
    _timers.remove(questionId)?.cancel();
    // Wait for an earlier save of this question so writes stay ordered.
    final previous = _inFlight[questionId];
    if (previous != null) await previous;
    final response = _pending.remove(questionId);
    if (response == null) return;
    final future = _save(questionId, response);
    _inFlight[questionId] = future;
    try {
      await future;
    } finally {
      _inFlight.removeWhere((id, f) => id == questionId && identical(f, future));
    }
  }

  Future<void> _save(String questionId, ExamResponse response) async {
    _set(questionId, SaveState.saving);
    try {
      await _api.saveAnswer(attemptId, questionId, response);
      if (!_pending.containsKey(questionId)) _set(questionId, SaveState.saved);
    } on ApiException catch (e) {
      lastError = e;
      // Keep the response so "Retry" / submit can try again.
      _pending.putIfAbsent(questionId, () => response);
      _set(questionId, SaveState.failed);
    }
  }

  /// Saves everything outstanding now (before submit, or as time runs out).
  Future<void> flush() async {
    final ids = {..._pending.keys, ..._timers.keys};
    await Future.wait([for (final id in ids) _flushOne(id)]);
    await Future.wait(_inFlight.values.toList());
  }

  void _set(String id, SaveState s) {
    states[id] = s;
    onStateChanged?.call();
  }

  void dispose() {
    for (final t in _timers.values) {
      t.cancel();
    }
    _timers.clear();
  }
}

class ExamAttemptScreen extends StatefulWidget {
  const ExamAttemptScreen({super.key, required this.examId});
  final String examId;

  @override
  State<ExamAttemptScreen> createState() => _ExamAttemptScreenState();
}

class _ExamAttemptScreenState extends State<ExamAttemptScreen> {
  Future<Attempt>? _future;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    _future ??= AppScope.read(context).learning.startAttempt(widget.examId);
  }

  @override
  Widget build(BuildContext context) {
    final api = AppScope.of(context).learning;
    return FutureBuilder<Attempt>(
      future: _future,
      builder: (context, snap) {
        if (snap.hasData) return AttemptView(attempt: snap.data!, api: api);
        return Scaffold(
          appBar: AppBar(title: const Text('Exam')),
          body: snap.hasError
              ? ErrorState(
                  error: snap.error!,
                  onRetry: () => setState(() => _future = api.startAttempt(widget.examId)),
                )
              : const LoadingView(label: 'Opening exam'),
        );
      },
    );
  }
}

class AttemptView extends StatefulWidget {
  const AttemptView({super.key, required this.attempt, required this.api, this.clock});
  final Attempt attempt;
  final LearningApi api;
  final DateTime Function()? clock;

  @override
  State<AttemptView> createState() => _AttemptViewState();
}

class _AttemptViewState extends State<AttemptView> {
  late final AnswerAutosaver _saver = AnswerAutosaver(
    attemptId: widget.attempt.attemptId,
    api: widget.api,
    onStateChanged: () {
      if (mounted) setState(() {});
    },
  );
  late final Map<String, ExamResponse> _responses = {
    for (final q in widget.attempt.questions)
      if (q.savedResponse != null) q.examQuestionId: q.savedResponse!,
  };
  Timer? _ticker;
  int _index = 0;
  bool _submitting = false;
  bool _flushedNearDeadline = false;

  DateTime _now() => (widget.clock ?? () => DateTime.now().toUtc())();
  Duration get _remaining => widget.attempt.deadlineUtc.difference(_now());

  @override
  void initState() {
    super.initState();
    _ticker = Timer.periodic(const Duration(seconds: 1), (_) => _tick());
  }

  void _tick() {
    if (!mounted) return;
    final left = _remaining;
    if (left <= const Duration(seconds: 5) && !_flushedNearDeadline) {
      _flushedNearDeadline = true;
      unawaited(_saver.flush());
    }
    if (left <= Duration.zero) {
      _ticker?.cancel();
      unawaited(_submit(auto: true));
      return;
    }
    setState(() {});
  }

  @override
  void dispose() {
    _ticker?.cancel();
    _saver.dispose();
    super.dispose();
  }

  void _onChanged(AttemptQuestion q, ExamResponse r) {
    setState(() => _responses[q.examQuestionId] = r);
    final typed = switch (q.type) {
      QuestionType.shortAnswer || QuestionType.longAnswer || QuestionType.fillBlank || QuestionType.numerical => true,
      _ => false,
    };
    _saver.update(q.examQuestionId, r, immediate: !typed);
  }

  int get _answeredCount => widget.attempt.questions.where((q) => !(_responses[q.examQuestionId]?.isEmpty ?? true)).length;

  Future<void> _submit({bool auto = false}) async {
    if (_submitting) return;
    if (!auto) {
      final unanswered = widget.attempt.questions.length - _answeredCount;
      final ok = await showDialog<bool>(
        context: context,
        builder: (context) => AlertDialog(
          title: const Text('Submit exam?'),
          content: Text(unanswered == 0
              ? 'You have answered every question. You cannot change your answers after submitting.'
              : 'You have $unanswered unanswered ${unanswered == 1 ? 'question' : 'questions'}. You cannot change your answers after submitting.'),
          actions: [
            TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Keep working')),
            FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Submit')),
          ],
        ),
      );
      if (ok != true || !mounted) return;
    }
    setState(() => _submitting = true);
    try {
      await _saver.flush();
      final result = await widget.api.submitAttempt(widget.attempt.attemptId);
      if (!mounted) return;
      _ticker?.cancel();
      if (auto) showSnack(context, "Time's up — your answers were submitted.");
      context.pushReplacement('/attempts/${result.attemptId}/result', extra: result);
    } on Object catch (e) {
      if (!mounted) return;
      setState(() => _submitting = false);
      showSnack(context, auto ? "Time's up, but we couldn't submit: ${describeError(e)} Tap Submit to try again." : describeError(e), error: true);
    }
  }

  Future<bool> _confirmLeave() async {
    final leave = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Leave the exam?'),
        content: const Text('Your saved answers are kept and you can resume, but the timer keeps running.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Stay')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Leave')),
        ],
      ),
    );
    if (leave == true) await _saver.flush();
    return leave ?? false;
  }

  @override
  Widget build(BuildContext context) {
    final a = widget.attempt;
    final t = Theme.of(context).textTheme;
    final questions = a.questions;
    final remaining = _remaining;
    final urgent = remaining <= const Duration(minutes: 5);
    final failed = _saver.states.values.any((s) => s == SaveState.failed);
    final saving = _saver.states.values.any((s) => s == SaveState.saving || s == SaveState.pending);
    final saveLabel = failed ? 'Some answers are not saved' : saving ? 'Saving…' : 'All answers saved';

    if (questions.isEmpty) {
      return Scaffold(appBar: AppBar(title: Text(a.examTitle)), body: const EmptyState(title: 'This exam has no questions'));
    }
    final q = questions[_index];

    return PopScope(
      canPop: false,
      onPopInvokedWithResult: (didPop, _) async {
        if (didPop || _submitting) return;
        if (await _confirmLeave() && context.mounted) Navigator.of(context).pop();
      },
      child: Scaffold(
        appBar: AppBar(
          title: Text(a.examTitle, overflow: TextOverflow.ellipsis),
          bottom: PreferredSize(
            preferredSize: const Size.fromHeight(44),
            child: Padding(
              padding: const EdgeInsets.fromLTRB(SsSpace.s4, 0, SsSpace.s4, SsSpace.s2),
              child: Row(children: [
                Semantics(
                  label: 'Time remaining ${formatCountdown(remaining)}',
                  liveRegion: urgent,
                  child: Container(
                    padding: const EdgeInsets.symmetric(horizontal: SsSpace.s3, vertical: SsSpace.s1),
                    decoration: BoxDecoration(
                      color: urgent ? SsColors.error.withValues(alpha: 0.12) : SsColors.orange50,
                      borderRadius: BorderRadius.circular(SsRadius.pill),
                    ),
                    child: Row(mainAxisSize: MainAxisSize.min, children: [
                      Icon(LucideIcons.timer, size: 18, color: urgent ? SsColors.error : SsColors.navy),
                      const SizedBox(width: SsSpace.s1),
                      Text(formatCountdown(remaining),
                          style: t.titleSmall?.copyWith(fontFeatures: const [FontFeature.tabularFigures()], color: urgent ? SsColors.error : SsColors.navy)),
                    ]),
                  ),
                ),
                const SizedBox(width: SsSpace.s3),
                Expanded(
                  child: Row(mainAxisAlignment: MainAxisAlignment.end, children: [
                    Icon(failed ? LucideIcons.cloudOff : saving ? LucideIcons.cloudUpload : LucideIcons.cloudCheck,
                        size: 18, color: failed ? SsColors.error : SsColors.slate),
                    const SizedBox(width: SsSpace.s1),
                    Flexible(child: Text(saveLabel, style: t.bodySmall?.copyWith(color: failed ? SsColors.error : null), overflow: TextOverflow.ellipsis)),
                  ]),
                ),
              ]),
            ),
          ),
        ),
        body: Column(children: [
          SizedBox(
            height: 60,
            child: ListView.separated(
              scrollDirection: Axis.horizontal,
              padding: const EdgeInsets.symmetric(horizontal: SsSpace.s4, vertical: SsSpace.s2),
              itemCount: questions.length,
              separatorBuilder: (_, _) => const SizedBox(width: SsSpace.s2),
              itemBuilder: (context, i) {
                final answered = !(_responses[questions[i].examQuestionId]?.isEmpty ?? true);
                final current = i == _index;
                return Semantics(
                  button: true,
                  selected: current,
                  label: 'Question ${i + 1}, ${answered ? 'answered' : 'not answered'}',
                  excludeSemantics: true,
                  child: InkWell(
                    borderRadius: BorderRadius.circular(SsRadius.control),
                    onTap: () => setState(() => _index = i),
                    child: Container(
                      width: kMinTouchTarget,
                      alignment: Alignment.center,
                      decoration: BoxDecoration(
                        color: current ? SsColors.blue : answered ? SsColors.blue50 : SsColors.white,
                        borderRadius: BorderRadius.circular(SsRadius.control),
                        border: Border.all(color: current ? SsColors.blue : SsColors.border),
                      ),
                      child: Text('${i + 1}', style: t.titleSmall?.copyWith(color: current ? SsColors.white : SsColors.navy)),
                    ),
                  ),
                );
              },
            ),
          ),
          const Divider(),
          Expanded(
            child: ListView(padding: const EdgeInsets.all(SsSpace.s4), children: [
              QuestionInput(
                key: ValueKey(q.examQuestionId),
                question: q,
                response: _responses[q.examQuestionId],
                enabled: !_submitting,
                index: _index,
                total: questions.length,
                onChanged: (r) => _onChanged(q, r),
              ),
              if (_saver.states[q.examQuestionId] == SaveState.failed) ...[
                const SizedBox(height: SsSpace.s2),
                Row(children: [
                  Expanded(
                    child: Text(_saver.lastError?.message ?? 'This answer was not saved.', style: t.bodySmall?.copyWith(color: SsColors.error)),
                  ),
                  TextButton(onPressed: () => _saver.update(q.examQuestionId, _responses[q.examQuestionId] ?? const ExamResponse(), immediate: true), child: const Text('Retry')),
                ]),
              ],
            ]),
          ),
          BottomBarSafeArea(
            top: false,
            child: Container(
              padding: const EdgeInsets.all(SsSpace.s3),
              decoration: const BoxDecoration(color: SsColors.white, border: Border(top: BorderSide(color: SsColors.border))),
              child: Row(children: [
                OutlinedButton(
                  onPressed: _index == 0 || _submitting ? null : () => setState(() => _index--),
                  child: const Text('Previous'),
                ),
                const SizedBox(width: SsSpace.s2),
                if (_index < questions.length - 1)
                  OutlinedButton(onPressed: _submitting ? null : () => setState(() => _index++), child: const Text('Next')),
                const Spacer(),
                FilledButton(
                  onPressed: _submitting ? null : () => _submit(),
                  child: _submitting
                      ? const SizedBox(width: 20, height: 20, child: CircularProgressIndicator(strokeWidth: 2, color: SsColors.white))
                      : Text('Submit ($_answeredCount/${questions.length})'),
                ),
              ]),
            ),
          ),
        ]),
      ),
    );
  }
}
