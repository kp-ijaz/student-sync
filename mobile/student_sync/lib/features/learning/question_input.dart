import 'package:flutter/material.dart';

import '../../core/models/enums.dart';
import '../../core/models/learning_models.dart';
import '../../core/theme/status_styles.dart';
import '../../core/theme/tokens.dart';
import '../../core/time/time_format.dart';

/// Renders one exam question by type and reports every change as an [ExamResponse].
/// Answer keys never reach the client; this widget only collects the response.
class QuestionInput extends StatefulWidget {
  const QuestionInput({super.key, required this.question, required this.response, required this.onChanged, this.enabled = true, this.index, this.total});

  final AttemptQuestion question;
  final ExamResponse? response;
  final ValueChanged<ExamResponse> onChanged;
  final bool enabled;
  final int? index;
  final int? total;

  @override
  State<QuestionInput> createState() => _QuestionInputState();
}

class _QuestionInputState extends State<QuestionInput> {
  late final TextEditingController _text = TextEditingController(text: _initialText());
  String? _numberError;

  String _initialText() {
    final r = widget.response;
    if (widget.question.type == QuestionType.numerical) return r?.number == null ? '' : formatMarks(r!.number!);
    return r?.text ?? '';
  }

  @override
  void dispose() {
    _text.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final q = widget.question;
    final t = Theme.of(context).textTheme;
    return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
      if (widget.index != null && widget.total != null)
        Text('Question ${widget.index! + 1} of ${widget.total} · ${formatMarks(q.marks)} ${q.marks == 1 ? 'mark' : 'marks'}', style: t.labelSmall),
      const SizedBox(height: SsSpace.s1),
      Semantics(header: true, child: Text(q.stem, style: t.titleMedium)),
      const SizedBox(height: SsSpace.s1),
      Text(StatusStyles.questionTypeLabel(q.type), style: t.bodySmall),
      const SizedBox(height: SsSpace.s3),
      _input(context),
    ]);
  }

  Widget _input(BuildContext context) {
    final q = widget.question;
    final r = widget.response;
    switch (q.type) {
      case QuestionType.singleChoice:
      case QuestionType.trueFalse:
        final selected = (r?.optionIds?.isNotEmpty ?? false) ? r!.optionIds!.first : null;
        return RadioGroup<String>(
          groupValue: selected,
          onChanged: (v) {
            if (!widget.enabled || v == null) return;
            widget.onChanged(ExamResponse(optionIds: [v]));
          },
          child: Column(children: [
            for (final o in q.options)
              _OptionFrame(
                selected: selected == o.id,
                child: RadioListTile<String>(
                  value: o.id,
                  enabled: widget.enabled,
                  title: Text(o.text),
                ),
              ),
          ]),
        );
      case QuestionType.multipleChoice:
        final chosen = {...?r?.optionIds};
        return Column(children: [
          for (final o in q.options)
            _OptionFrame(
              selected: chosen.contains(o.id),
              child: CheckboxListTile(
                value: chosen.contains(o.id),
                controlAffinity: ListTileControlAffinity.leading,
                title: Text(o.text),
                onChanged: !widget.enabled
                    ? null
                    : (v) {
                        final next = {...chosen};
                        if (v ?? false) {
                          next.add(o.id);
                        } else {
                          next.remove(o.id);
                        }
                        // Keep the server's option order for a stable payload.
                        widget.onChanged(ExamResponse(optionIds: [for (final x in q.options) if (next.contains(x.id)) x.id]));
                      },
              ),
            ),
        ]);
      case QuestionType.numerical:
        return TextField(
          key: const Key('question.number'),
          controller: _text,
          enabled: widget.enabled,
          keyboardType: const TextInputType.numberWithOptions(decimal: true, signed: true),
          decoration: InputDecoration(labelText: 'Your answer (number)', errorText: _numberError),
          onChanged: (v) {
            final trimmed = v.trim().replaceAll(',', '.');
            if (trimmed.isEmpty) {
              setState(() => _numberError = null);
              widget.onChanged(const ExamResponse());
              return;
            }
            final n = num.tryParse(trimmed);
            setState(() => _numberError = n == null ? 'Enter a number, e.g. 12.5' : null);
            if (n != null) widget.onChanged(ExamResponse(number: n));
          },
        );
      case QuestionType.shortAnswer:
      case QuestionType.fillBlank:
      case QuestionType.longAnswer:
        final long = q.type == QuestionType.longAnswer;
        return TextField(
          key: const Key('question.text'),
          controller: _text,
          enabled: widget.enabled,
          minLines: long ? 6 : 1,
          maxLines: long ? 20 : 3,
          maxLength: long ? 10000 : 500,
          decoration: InputDecoration(
            labelText: q.type == QuestionType.fillBlank ? 'Word or phrase for the blank' : 'Your answer',
            alignLabelWithHint: long,
          ),
          onChanged: (v) => widget.onChanged(ExamResponse(text: v)),
        );
      case QuestionType.matching:
        final matches = {...?r?.matches};
        final choices = q.matchChoices ?? const <String>[];
        return Column(children: [
          for (final o in q.options)
            Padding(
              padding: const EdgeInsets.only(bottom: SsSpace.s3),
              child: DropdownButtonFormField<String>(
                key: Key('match.${o.id}'),
                initialValue: choices.contains(matches[o.id]) ? matches[o.id] : null,
                isExpanded: true,
                decoration: InputDecoration(labelText: o.text),
                hint: const Text('Choose a match'),
                items: [for (final c in choices) DropdownMenuItem(value: c, child: Text(c))],
                onChanged: !widget.enabled
                    ? null
                    : (v) {
                        final next = {...matches};
                        if (v == null) {
                          next.remove(o.id);
                        } else {
                          next[o.id] = v;
                        }
                        widget.onChanged(ExamResponse(matches: next));
                      },
              ),
            ),
        ]);
    }
  }
}

class _OptionFrame extends StatelessWidget {
  const _OptionFrame({required this.selected, required this.child});
  final bool selected;
  final Widget child;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(bottom: SsSpace.s2),
        child: DecoratedBox(
          decoration: BoxDecoration(
            color: selected ? SsColors.blue50 : SsColors.white,
            borderRadius: BorderRadius.circular(SsRadius.control),
            border: Border.all(color: selected ? SsColors.blue : SsColors.border),
          ),
          child: child,
        ),
      );
}
