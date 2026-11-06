import 'package:flutter/material.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/app_services.dart';
import '../../core/models/enums.dart';
import '../../core/models/session_models.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/states.dart';

bool canJoin(SessionStatus status, bool hasLink) => hasLink && (status == SessionStatus.scheduled || status == SessionStatus.live);

/// Joinable right now: live, or starting within 15 minutes (and not over). Lists of
/// many classes show Join only then, so it is never offered days ahead.
bool joinableNow(SessionListItem s, DateTime nowUtc) =>
    canJoin(s.status, s.hasMeetingLink) &&
    (s.status == SessionStatus.live || (s.startUtc.difference(nowUtc) <= const Duration(minutes: 15) && s.endUtc.isAfter(nowUtc)));

/// "Join class" for a list item: fetches the session (the list payload only says a
/// link exists) and opens the meeting URL in the meeting app / browser.
class JoinSessionButton extends StatefulWidget {
  const JoinSessionButton({super.key, required this.session, this.expand = false});
  final SessionListItem session;
  final bool expand;

  @override
  State<JoinSessionButton> createState() => _JoinSessionButtonState();
}

class _JoinSessionButtonState extends State<JoinSessionButton> {
  bool _busy = false;

  Future<void> _join() async {
    setState(() => _busy = true);
    try {
      final detail = await AppScope.read(context).sessions.detail(widget.session.id);
      if (!mounted) return;
      final url = detail.meetingUrl;
      if (url == null || url.isEmpty) {
        showSnack(context, 'No meeting link has been added for this class yet.', error: true);
      } else {
        await openExternal(context, url);
      }
    } on Object catch (e) {
      if (mounted) showSnack(context, describeError(e), error: true);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    if (!canJoin(widget.session.status, widget.session.hasMeetingLink)) return const SizedBox.shrink();
    final button = FilledButton.icon(
      style: widget.expand ? FilledButton.styleFrom(minimumSize: const Size.fromHeight(50)) : null,
      onPressed: _busy ? null : _join,
      icon: _busy ? const SizedBox(width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2)) : const Icon(LucideIcons.video, size: 20),
      label: Text(widget.session.status == SessionStatus.live ? 'Join live class' : 'Join class'),
    );
    return widget.expand ? SizedBox(width: double.infinity, child: button) : button;
  }
}
