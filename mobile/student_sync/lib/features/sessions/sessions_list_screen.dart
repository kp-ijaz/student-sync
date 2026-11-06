import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/app_services.dart';
import '../../core/models/session_models.dart';
import '../../core/theme/tokens.dart';
import '../../core/widgets/async_view.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/notification_bell.dart';
import '../../core/widgets/session_card.dart';
import '../../core/widgets/states.dart';
import 'join_button.dart';
import 'month_calendar.dart';

enum _ClassesView { calendar, upcoming, past }

/// "My Classes" (student) / "Classes" (teacher): a month calendar plus upcoming and past
/// lists of every teaching mode — batch, group, private group and one-to-one.
class SessionsListScreen extends StatefulWidget {
  const SessionsListScreen({super.key, required this.title, this.showTeacher = true, this.startWithCalendar = false, this.showVideosAction = false});
  final String title;
  final bool showTeacher;

  /// Students open on the calendar; teachers on the upcoming list.
  final bool startWithCalendar;

  /// Adds an app-bar shortcut to every pre-recorded video (teachers; students have it under Learning).
  final bool showVideosAction;

  @override
  State<SessionsListScreen> createState() => _SessionsListScreenState();
}

class _SessionsListScreenState extends State<SessionsListScreen> {
  late _ClassesView _view = widget.startWithCalendar ? _ClassesView.calendar : _ClassesView.upcoming;

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    return Scaffold(
      appBar: AppBar(
        title: Text(widget.title),
        actions: [
          if (widget.showVideosAction) ...[
            IconButton(
              tooltip: 'Pre-recorded videos',
              style: IconButton.styleFrom(backgroundColor: SsColors.white, side: const BorderSide(color: SsColors.hairline)),
              icon: const Icon(LucideIcons.monitorPlay, size: 20),
              onPressed: () => context.push('/videos'),
            ),
            const SizedBox(width: SsSpace.s2),
          ],
          ...tabBarActions,
        ],
      ),
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(SsSpace.s4, SsSpace.s3, SsSpace.s4, 0),
            child: _ViewSwitch(selected: _view, onChanged: (v) => setState(() => _view = v)),
          ),
          Expanded(child: _view == _ClassesView.calendar ? MonthCalendar(showTeacher: widget.showTeacher) : _list(services)),
        ],
      ),
    );
  }

  Widget _list(AppServices services) {
    final past = _view == _ClassesView.past;
    return AsyncView<List<SessionListItem>>(
      key: ValueKey(past),
      load: () {
        final now = DateTime.now().toUtc();
        return past ? services.sessions.list(toUtc: now, limit: 50) : services.sessions.list(fromUtc: now, limit: 100);
      },
      isEmpty: (d) => d.isEmpty,
      empty: (context, _) => EmptyState(
        icon: LucideIcons.calendarX,
        title: past ? 'No past classes yet' : 'No upcoming classes',
        message: past ? 'Classes you have attended will appear here.' : 'When a class is scheduled for you it will appear here.',
      ),
      builder: (context, sessions, reload) => _SessionGroups(sessions: sessions, reload: reload, showTeacher: widget.showTeacher, allowJoin: !past),
    );
  }
}

/// Calendar / Upcoming / Past. Always one line: icons only appear when there is room,
/// and labels shrink rather than wrap at large text sizes.
class _ViewSwitch extends StatelessWidget {
  const _ViewSwitch({required this.selected, required this.onChanged});
  final _ClassesView selected;
  final ValueChanged<_ClassesView> onChanged;

  static const _options = [
    (_ClassesView.calendar, 'Calendar', LucideIcons.calendarDays),
    (_ClassesView.upcoming, 'Upcoming', LucideIcons.calendar),
    (_ClassesView.past, 'Past', LucideIcons.history),
  ];

  @override
  Widget build(BuildContext context) => LayoutBuilder(builder: (context, c) {
        final roomy = c.maxWidth >= 480 && MediaQuery.textScalerOf(context).scale(14) <= 16;
        return SsSegmented<_ClassesView>(
          segments: [for (final (value, label, icon) in _options) (value: value, label: label, icon: icon)],
          selected: selected,
          showIcons: roomy,
          onChanged: onChanged,
        );
      });
}

class _SessionGroups extends StatelessWidget {
  const _SessionGroups({required this.sessions, required this.reload, required this.showTeacher, required this.allowJoin});
  final List<SessionListItem> sessions;
  final Future<void> Function() reload;
  final bool showTeacher;
  final bool allowJoin;

  @override
  Widget build(BuildContext context) {
    final time = AppScope.of(context).time;
    final now = DateTime.now().toUtc();
    final children = <Widget>[];
    String? lastKey;
    for (final s in sessions) {
      final key = time.dayKey(s.startUtc);
      if (key != lastKey) {
        children.add(SectionHeader(time.dayLabel(s.startUtc, now)));
        lastKey = key;
      }
      children.add(
        Padding(
          padding: const EdgeInsets.only(bottom: SsSpace.s3),
          child: SessionCard(
            session: s,
            time: time,
            showTeacher: showTeacher,
            onTap: () => context.push('/sessions/${s.id}'),
            action: allowJoin && canJoin(s.status, s.hasMeetingLink) ? JoinSessionButton(session: s) : null,
          ),
        ),
      );
    }
    return RefreshIndicator(
      onRefresh: reload,
      child: ListView(padding: pagePadding(context), children: children),
    );
  }
}
