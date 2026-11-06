import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/api/api_exception.dart';
import '../../core/app_services.dart';
import '../../core/models/doubt_models.dart';
import '../../core/models/enums.dart';
import '../../core/theme/status_styles.dart';
import '../../core/theme/tokens.dart';
import '../../core/widgets/async_view.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/states.dart';

class DoubtDetailScreen extends StatelessWidget {
  const DoubtDetailScreen({super.key, required this.doubtId});
  final String doubtId;

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    return Scaffold(
      appBar: AppBar(title: const Text('Doubt')),
      body: AsyncView<DoubtDetail>(
        load: () => services.doubts.detail(doubtId),
        builder: (context, d, reload) => _DoubtBody(doubt: d, reload: reload),
      ),
    );
  }
}

class _DoubtBody extends StatefulWidget {
  const _DoubtBody({required this.doubt, required this.reload});
  final DoubtDetail doubt;
  final Future<void> Function() reload;

  @override
  State<_DoubtBody> createState() => _DoubtBodyState();
}

class _DoubtBodyState extends State<_DoubtBody> {
  final _reply = TextEditingController();
  bool _markAnswered = true;
  bool _busy = false;
  String? _replyError;

  @override
  void dispose() {
    _reply.dispose();
    super.dispose();
  }

  bool get _canReply => widget.doubt.canAnswer || widget.doubt.canConfirm;

  Future<void> _sendReply() async {
    final body = _reply.text.trim();
    if (body.isEmpty) {
      setState(() => _replyError = 'Write a reply first.');
      return;
    }
    setState(() {
      _busy = true;
      _replyError = null;
    });
    try {
      await AppScope.read(context).doubts.reply(widget.doubt.id, body: body, markAnswered: widget.doubt.canAnswer && _markAnswered);
      if (!mounted) return;
      _reply.clear();
      showSnack(context, widget.doubt.canAnswer && _markAnswered ? 'Reply sent and marked as answered.' : 'Reply sent.');
      await widget.reload();
    } on ApiException catch (e) {
      if (!mounted) return;
      final field = e.fieldError('body');
      if (field != null) {
        setState(() => _replyError = field);
      } else {
        showSnack(context, e.message, error: true);
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _confirm({required bool resolved}) async {
    String? comment;
    if (!resolved) {
      final controller = TextEditingController();
      final ok = await showDialog<bool>(
        context: context,
        builder: (context) => AlertDialog(
          title: const Text('Still stuck?'),
          content: TextField(
            controller: controller,
            maxLength: 1000,
            maxLines: 4,
            minLines: 2,
            decoration: const InputDecoration(labelText: 'What is still unclear? (optional)'),
          ),
          actions: [
            TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
            FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Reopen doubt')),
          ],
        ),
      );
      comment = controller.text;
      controller.dispose();
      if (ok != true) return;
    }
    if (!mounted) return;
    setState(() => _busy = true);
    try {
      await AppScope.read(context).doubts.confirm(widget.doubt.id, resolved: resolved, comment: comment);
      if (!mounted) return;
      showSnack(context, resolved ? 'Marked as resolved. Thanks for confirming!' : 'Doubt reopened. Your teacher has been told.');
      await widget.reload();
    } on Object catch (e) {
      if (mounted) showSnack(context, describeError(e), error: true);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final d = widget.doubt;
    final t = Theme.of(context).textTheme;
    final time = AppScope.of(context).time;
    final awaitingStudent = d.canConfirm && (d.status == DoubtStatus.answered || d.status == DoubtStatus.pendingConfirmation);
    final closed = d.status == DoubtStatus.resolved;

    return Column(children: [
      Expanded(
        child: RefreshIndicator(
          onRefresh: widget.reload,
          child: ListView(padding: pagePadding(context), children: [
            const SizedBox(height: SsSpace.s2),
            Semantics(header: true, child: Text(d.title, style: t.headlineSmall)),
            const SizedBox(height: SsSpace.s2),
            Wrap(spacing: SsSpace.s2, runSpacing: SsSpace.s2, children: [
              StatusBadge(StatusStyles.doubt(d.status)),
              if (d.priority == DoubtPriority.high || d.priority == DoubtPriority.urgent) StatusBadge(StatusStyles.priority(d.priority)),
              if (d.isPrivate) const TagPill('Private', icon: LucideIcons.lock),
            ]),
            const SizedBox(height: SsSpace.s3),
            SsCard(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(d.description, style: t.bodyMedium),
                const Divider(height: SsSpace.s5),
                InfoRow(icon: LucideIcons.user, label: 'Asked by', value: d.studentName),
                if (d.subjectName != null) InfoRow(icon: LucideIcons.bookOpen, label: 'Subject', value: d.subjectName!),
                if (d.assignedTeacherName != null) InfoRow(icon: LucideIcons.graduationCap, label: 'Teacher', value: d.assignedTeacherName!),
                if (d.sessionTitle != null && d.sessionId != null)
                  InfoRow(
                    icon: LucideIcons.presentation,
                    label: 'From class',
                    value: d.sessionTitle!,
                    trailing: TextButton(onPressed: () => context.push('/sessions/${d.sessionId}'), child: const Text('Open')),
                  ),
                if (d.followUpSessionStartUtc != null)
                  InfoRow(
                    icon: LucideIcons.calendarCheck,
                    label: 'Will be discussed in',
                    value: '${d.followUpSessionTitle ?? 'class'} · ${time.dateTime(d.followUpSessionStartUtc!)}',
                  ),
              ]),
            ),
            if (awaitingStudent) ...[
              const SizedBox(height: SsSpace.s4),
              SsCard(
                color: SsColors.orange50,
                accent: SsColors.orange,
                child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  Text('Did this answer your question?', style: t.titleSmall),
                  const SizedBox(height: SsSpace.s3),
                  Row(children: [
                    Expanded(child: OutlinedButton(onPressed: _busy ? null : () => _confirm(resolved: false), child: const Text('Still stuck'))),
                    const SizedBox(width: SsSpace.s3),
                    Expanded(child: FilledButton(onPressed: _busy ? null : () => _confirm(resolved: true), child: const Text('Yes, resolved'))),
                  ]),
                ]),
              ),
            ],
            SectionHeader('Conversation (${d.messages.length})'),
            if (d.messages.isEmpty)
              Text('No replies yet. Your teacher will reply here.', style: t.bodySmall)
            else
              for (final m in d.messages) _MessageBubble(message: m, when: time.dateTime(m.createdAtUtc)),
            if (closed && d.canConfirm) ...[
              const SizedBox(height: SsSpace.s3),
              Align(
                alignment: Alignment.centerLeft,
                child: TextButton.icon(
                  onPressed: _busy ? null : () => _confirm(resolved: false),
                  icon: const Icon(LucideIcons.rotateCcw),
                  label: const Text('Reopen this doubt'),
                ),
              ),
            ],
            if (d.history.isNotEmpty) ...[
              const SectionHeader('History'),
              for (final h in d.history)
                Padding(
                  padding: const EdgeInsets.only(bottom: SsSpace.s1),
                  child: Text(
                    '${time.dateTime(h.atUtc)} · ${StatusStyles.doubt(h.from).label} → ${StatusStyles.doubt(h.to).label} · ${h.changedBy}${h.note == null ? '' : ' — ${h.note}'}',
                    style: t.bodySmall,
                  ),
                ),
            ],
          ]),
        ),
      ),
      if (_canReply && !closed)
        BottomBarSafeArea(
          top: false,
          child: Container(
            padding: const EdgeInsets.fromLTRB(SsSpace.s4, SsSpace.s3, SsSpace.s4, SsSpace.s3),
            decoration: const BoxDecoration(color: SsColors.white, border: Border(top: BorderSide(color: SsColors.border))),
            child: Column(mainAxisSize: MainAxisSize.min, children: [
              TextField(
                controller: _reply,
                enabled: !_busy,
                minLines: 1,
                maxLines: 5,
                maxLength: 8000,
                decoration: InputDecoration(
                  labelText: d.canAnswer ? 'Reply to ${d.studentName.split(' ').first}' : 'Add a reply',
                  errorText: _replyError,
                  counterText: '',
                ),
              ),
              const SizedBox(height: SsSpace.s2),
              Row(children: [
                if (d.canAnswer)
                  Expanded(
                    child: CheckboxListTile(
                      contentPadding: EdgeInsets.zero,
                      dense: true,
                      controlAffinity: ListTileControlAffinity.leading,
                      value: _markAnswered,
                      onChanged: _busy ? null : (v) => setState(() => _markAnswered = v ?? false),
                      title: const Text('Mark as answered'),
                    ),
                  )
                else
                  const Spacer(),
                FilledButton.icon(
                  onPressed: _busy ? null : _sendReply,
                  icon: _busy ? const SizedBox(width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2)) : const Icon(LucideIcons.sendHorizontal),
                  label: const Text('Send'),
                ),
              ]),
            ]),
          ),
        ),
    ]);
  }
}

class _MessageBubble extends StatelessWidget {
  const _MessageBubble({required this.message, required this.when});
  final DoubtMessage message;
  final String when;

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).textTheme;
    final teacher = message.isTeacherReply;
    return Align(
      alignment: teacher ? Alignment.centerLeft : Alignment.centerRight,
      child: ConstrainedBox(
        constraints: BoxConstraints(maxWidth: MediaQuery.sizeOf(context).width * 0.85),
        child: Container(
          margin: const EdgeInsets.only(bottom: SsSpace.s2),
          padding: const EdgeInsets.all(SsSpace.s3),
          decoration: BoxDecoration(
            color: teacher ? SsColors.white : SsColors.blue50,
            borderRadius: BorderRadius.circular(SsRadius.card),
            border: Border.all(color: SsColors.border),
          ),
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text('${message.authorName}${teacher ? ' · Teacher' : ''}', style: t.labelMedium?.copyWith(color: SsColors.navy)),
            const SizedBox(height: SsSpace.s1),
            SelectableText(message.body, style: t.bodyMedium),
            const SizedBox(height: SsSpace.s1),
            Text(when, style: t.labelSmall),
            if (message.attachments.isNotEmpty)
              Text('${message.attachments.length} attachment(s) — open on the web app', style: t.labelSmall),
          ]),
        ),
      ),
    );
  }
}
