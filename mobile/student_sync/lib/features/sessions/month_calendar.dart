import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/app_services.dart';
import '../../core/models/session_models.dart';
import '../../core/theme/status_styles.dart';
import '../../core/theme/tokens.dart';
import '../../core/time/time_format.dart';
import '../../core/widgets/async_view.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/session_card.dart';
import 'join_button.dart';

/// The days shown for [month]: whole Monday-first weeks covering the month, as
/// date-only values (`DateTime.utc(y, m, d)`). Matches the web calendar.
List<DateTime> monthGridDays(DateTime month) {
  final first = DateTime.utc(month.year, month.month);
  final start = first.subtract(Duration(days: first.weekday - DateTime.monday));
  final last = DateTime.utc(month.year, month.month + 1, 0);
  final end = last.add(Duration(days: DateTime.sunday - last.weekday));
  return [for (var d = start; !d.isAfter(end); d = DateTime.utc(d.year, d.month, d.day + 1)) d];
}

/// Classes grouped by the viewer's calendar day they start on.
Map<DateTime, List<SessionListItem>> sessionsByDay(List<SessionListItem> sessions, TimeFormatter time) {
  final map = <DateTime, List<SessionListItem>>{};
  for (final s in sessions) {
    (map[time.viewerDate(s.startUtc)] ??= []).add(s);
  }
  return map;
}

/// Month view of the user's classes. Tapping a day lists that day's classes below.
class MonthCalendar extends StatefulWidget {
  const MonthCalendar({super.key, required this.showTeacher});
  final bool showTeacher;

  @override
  State<MonthCalendar> createState() => _MonthCalendarState();
}

class _MonthCalendarState extends State<MonthCalendar> {
  DateTime? _month;
  DateTime? _selected;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (_month == null) {
      final today = AppScope.of(context).time.viewerDate(DateTime.now().toUtc());
      _month = DateTime.utc(today.year, today.month);
      _selected = today;
    }
  }

  void _shift(int months) => setState(() {
        _month = DateTime.utc(_month!.year, _month!.month + months);
        _selected = null;
      });

  void _goToday(DateTime today) => setState(() {
        _month = DateTime.utc(today.year, today.month);
        _selected = today;
      });

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    final time = services.time;
    final month = _month!;
    final days = monthGridDays(month);
    final today = time.viewerDate(DateTime.now().toUtc());
    final t = Theme.of(context).textTheme;
    final isCurrentMonth = month.year == today.year && month.month == today.month;

    final header = Padding(
      padding: const EdgeInsets.fromLTRB(SsSpace.s2, SsSpace.s2, SsSpace.s2, 0),
      child: Row(children: [
        IconButton(tooltip: 'Previous month', icon: const Icon(LucideIcons.chevronLeft), onPressed: () => _shift(-1)),
        Expanded(
          child: Semantics(
            header: true,
            liveRegion: true,
            child: Text(DateFormat('MMMM y').format(month), style: t.titleMedium, textAlign: TextAlign.center),
          ),
        ),
        if (!isCurrentMonth) TextButton(onPressed: () => _goToday(today), child: const Text('Today')),
        IconButton(tooltip: 'Next month', icon: const Icon(LucideIcons.chevronRight), onPressed: () => _shift(1)),
      ]),
    );

    return Column(children: [
      header,
      Expanded(
        child: AsyncView<List<SessionListItem>>(
          key: ValueKey(month),
          loadingLabel: 'Loading calendar',
          load: () => services.sessions.list(
            fromUtc: time.startOfViewerDayUtc(days.first),
            toUtc: time.startOfViewerDayUtc(days.last.add(const Duration(days: 1))),
            limit: 500,
          ),
          builder: (context, sessions, reload) {
            final byDay = sessionsByDay(sessions, time);
            final selected = _selected;
            final dayItems = selected == null ? null : (byDay[selected] ?? const <SessionListItem>[]);
            final monthCount = sessions.where((s) {
              final d = time.viewerDate(s.startUtc);
              return d.year == month.year && d.month == month.month;
            }).length;
            final now = DateTime.now().toUtc();
            return RefreshIndicator(
              onRefresh: reload,
              child: ListView(padding: pagePadding(context), children: [
                Text('Times shown in ${time.viewerZoneId}.', style: t.bodySmall),
                const SizedBox(height: SsSpace.s2),
                _Grid(
                  days: days,
                  month: month,
                  today: today,
                  selected: selected,
                  byDay: byDay,
                  onSelect: (d) => setState(() => _selected = d == _selected ? null : d),
                ),
                if (dayItems == null) ...[
                  SectionHeader(monthCount == 0 ? 'No classes this month' : '$monthCount ${monthCount == 1 ? 'class' : 'classes'} this month'),
                  if (monthCount > 0) Text('Tap a day to see its classes.', style: t.bodySmall),
                ] else ...[
                  SectionHeader(DateFormat('EEEE d MMMM').format(selected!)),
                  if (dayItems.isEmpty)
                    Text('No classes on this day.', style: t.bodyMedium)
                  else
                    for (final s in dayItems)
                      Padding(
                        padding: const EdgeInsets.only(bottom: SsSpace.s3),
                        child: SessionCard(
                          session: s,
                          time: time,
                          showTeacher: widget.showTeacher,
                          onTap: () => context.push('/sessions/${s.id}'),
                          action: s.endUtc.isAfter(now) && canJoin(s.status, s.hasMeetingLink) ? JoinSessionButton(session: s) : null,
                        ),
                      ),
                ],
              ]),
            );
          },
        ),
      ),
    ]);
  }
}

class _Grid extends StatelessWidget {
  const _Grid({required this.days, required this.month, required this.today, required this.selected, required this.byDay, required this.onSelect});
  final List<DateTime> days;
  final DateTime month;
  final DateTime today;
  final DateTime? selected;
  final Map<DateTime, List<SessionListItem>> byDay;
  final ValueChanged<DateTime> onSelect;

  static const _weekdays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).textTheme;
    return SsCard(
      padding: const EdgeInsets.all(SsSpace.s2),
      child: Column(children: [
        ExcludeSemantics(
          child: Row(children: [
            for (final d in _weekdays)
              Expanded(child: Text(d, textAlign: TextAlign.center, style: t.labelSmall?.copyWith(color: SsColors.textBody))),
          ]),
        ),
        const SizedBox(height: SsSpace.s1),
        for (var w = 0; w < days.length; w += 7)
          Row(children: [
            for (final d in days.sublist(w, w + 7))
              Expanded(
                child: _DayCell(
                  day: d,
                  inMonth: d.month == month.month,
                  isToday: d == today,
                  isSelected: d == selected,
                  sessions: byDay[d] ?? const [],
                  onTap: () => onSelect(d),
                ),
              ),
          ]),
      ]),
    );
  }
}

class _DayCell extends StatelessWidget {
  const _DayCell({required this.day, required this.inMonth, required this.isToday, required this.isSelected, required this.sessions, required this.onTap});
  final DateTime day;
  final bool inMonth;
  final bool isToday;
  final bool isSelected;
  final List<SessionListItem> sessions;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final count = sessions.length;
    final label = '${DateFormat('EEEE d MMMM').format(day)}${isToday ? ', today' : ''}, '
        '${count == 0 ? 'no classes' : '$count ${count == 1 ? 'class' : 'classes'}'}';
    final numberColor = isSelected ? SsColors.white : (inMonth ? SsColors.textBody : SsColors.textSecondary);
    return Semantics(
      button: true,
      selected: isSelected,
      label: label,
      excludeSemantics: true,
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(SsRadius.control),
        child: SizedBox(
          height: 52,
          child: Column(mainAxisAlignment: MainAxisAlignment.center, children: [
            Container(
              width: 32,
              height: 32,
              alignment: Alignment.center,
              decoration: BoxDecoration(
                shape: BoxShape.circle,
                color: isSelected ? SsColors.blue : null,
                border: isToday && !isSelected ? Border.all(color: SsColors.blue, width: 2) : null,
              ),
              child: Text(
                '${day.day}',
                style: TextStyle(fontFamily: 'Inter', fontSize: 14, fontWeight: isToday || isSelected ? FontWeight.w600 : FontWeight.w400, color: numberColor),
              ),
            ),
            const SizedBox(height: 2),
            SizedBox(
              height: 6,
              child: Row(mainAxisAlignment: MainAxisAlignment.center, children: [
                for (final s in sessions.take(3))
                  Container(
                    width: 6,
                    height: 6,
                    margin: const EdgeInsets.symmetric(horizontal: 1),
                    decoration: BoxDecoration(
                      shape: BoxShape.circle,
                      color: StatusStyles.session(s.status).color,
                    ),
                  ),
              ]),
            ),
          ]),
        ),
      ),
    );
  }
}
