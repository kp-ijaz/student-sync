import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/app_services.dart';
import '../../core/models/enums.dart';
import '../../core/models/session_models.dart';
import '../../core/theme/status_styles.dart';
import '../../core/theme/tokens.dart';
import '../../core/time/time_format.dart';
import '../../core/widgets/async_view.dart';
import '../../core/widgets/avatar.dart';
import '../../core/widgets/class_cover.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/illustration.dart';
import '../../core/widgets/states.dart';
import 'join_button.dart';
import 'month_calendar.dart';

/// A colour per subject so the same subject looks the same every day.
Color subjectColor(String? subject) {
  const palette = [SsColors.blue, SsColors.success, SsColors.orangeDeep, Color(0xFF7C3AED), Color(0xFF0891B2), Color(0xFFDB2777), SsColors.navy];
  final key = (subject ?? '').toLowerCase();
  if (key.isEmpty) return SsColors.slate;
  return palette[key.codeUnits.fold<int>(0, (a, c) => (a * 31 + c) & 0x7fffffff) % palette.length];
}

enum _View { day, month }

/// The student's timetable: a week strip and the chosen day hour by hour
/// ("14:30–15:30 English, 15:30–16:30 Mathematics …"), with a month view.
class TimetableScreen extends StatefulWidget {
  const TimetableScreen({super.key});

  @override
  State<TimetableScreen> createState() => _TimetableScreenState();
}

class _TimetableScreenState extends State<TimetableScreen> {
  _View _view = _View.day;

  /// Today in the viewer's zone until the user picks another day.
  late DateTime _day = AppScope.read(context).time.viewerDate(DateTime.now().toUtc());

  DateTime get _weekStart => _day.subtract(Duration(days: _day.weekday - DateTime.monday));

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Timetable')),
      body: Column(children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(SsSpace.s4, SsSpace.s1, SsSpace.s4, SsSpace.s3),
          child: SsSegmented<_View>(
            segments: const [
              (value: _View.day, label: 'Day', icon: LucideIcons.clock),
              (value: _View.month, label: 'Month', icon: LucideIcons.calendarDays),
            ],
            selected: _view,
            showIcons: true,
            onChanged: (v) => setState(() => _view = v),
          ),
        ),
        Expanded(
          child: _view == _View.month
              ? const MonthCalendar(showTeacher: true)
              : _WeekAgenda(
                  key: ValueKey(_weekStart),
                  weekStart: _weekStart,
                  day: _day,
                  onDay: (d) => setState(() => _day = d),
                  onWeek: (delta) => setState(() => _day = _day.add(Duration(days: 7 * delta))),
                ),
        ),
      ]),
    );
  }
}

class _WeekAgenda extends StatelessWidget {
  const _WeekAgenda({super.key, required this.weekStart, required this.day, required this.onDay, required this.onWeek});
  final DateTime weekStart;
  final DateTime day;
  final ValueChanged<DateTime> onDay;
  final ValueChanged<int> onWeek;

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    final time = services.time;
    final days = [for (var i = 0; i < 7; i++) weekStart.add(Duration(days: i))];
    return AsyncView<List<SessionListItem>>(
      load: () => services.sessions.list(
        fromUtc: time.startOfViewerDayUtc(days.first),
        toUtc: time.startOfViewerDayUtc(days.last.add(const Duration(days: 1))),
        limit: 200,
      ),
      loadingLabel: 'Loading timetable',
      builder: (context, sessions, reload) {
        final byDay = sessionsByDay(sessions, time);
        final today = byDay[day] ?? const <SessionListItem>[];
        return RefreshIndicator(
          onRefresh: reload,
          child: ListView(padding: pagePadding(context), children: [
            _WeekStrip(days: days, selected: day, busyDays: byDay.keys.toSet(), onDay: onDay, onWeek: onWeek, time: time),
            const SizedBox(height: SsSpace.s4),
            Row(children: [
              Expanded(
                child: Semantics(
                  header: true,
                  child: Text(DateFormat('EEEE d MMMM').format(day), style: Theme.of(context).textTheme.titleLarge),
                ),
              ),
              Text('${today.length} ${today.length == 1 ? 'class' : 'classes'}', style: Theme.of(context).textTheme.labelSmall),
            ]),
            Text('Times shown in ${time.viewerZoneId}.', style: Theme.of(context).textTheme.bodySmall),
            const SizedBox(height: SsSpace.s3),
            if (today.isEmpty)
              const Padding(
                padding: EdgeInsets.only(top: SsSpace.s4),
                child: EmptyState(
                  icon: LucideIcons.calendarX,
                  illustration: Illustration.allDone,
                  title: 'No classes on this day',
                  message: 'Pick another day above, or use the arrows to change week.',
                ),
              )
            else
              for (final s in today) TimetableSlot(session: s, time: time),
          ]),
        );
      },
    );
  }
}

class _WeekStrip extends StatelessWidget {
  const _WeekStrip({required this.days, required this.selected, required this.busyDays, required this.onDay, required this.onWeek, required this.time});
  final List<DateTime> days;
  final DateTime selected;
  final Set<DateTime> busyDays;
  final ValueChanged<DateTime> onDay;
  final ValueChanged<int> onWeek;
  final TimeFormatter time;

  @override
  Widget build(BuildContext context) {
    final today = time.viewerDate(DateTime.now().toUtc());
    return SsCard(
      padding: const EdgeInsets.fromLTRB(SsSpace.s1, SsSpace.s2, SsSpace.s1, SsSpace.s3),
      child: Column(children: [
        Row(children: [
          IconButton(tooltip: 'Previous week', onPressed: () => onWeek(-1), icon: const Icon(LucideIcons.chevronLeft, size: 20)),
          Expanded(
            child: Text(
              '${DateFormat('d MMM').format(days.first)} – ${DateFormat('d MMM y').format(days.last)}',
              textAlign: TextAlign.center,
              style: Theme.of(context).textTheme.titleSmall,
            ),
          ),
          IconButton(tooltip: 'Next week', onPressed: () => onWeek(1), icon: const Icon(LucideIcons.chevronRight, size: 20)),
        ]),
        Row(children: [
          for (final d in days)
            Expanded(
              child: _DayChip(
                date: d,
                selected: d == selected,
                today: d == today,
                busy: busyDays.contains(d),
                onTap: () => onDay(d),
              ),
            ),
        ]),
      ]),
    );
  }
}

class _DayChip extends StatelessWidget {
  const _DayChip({required this.date, required this.selected, required this.today, required this.busy, required this.onTap});
  final DateTime date;
  final bool selected;
  final bool today;
  final bool busy;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final fg = selected ? SsColors.white : SsColors.navy;
    return Semantics(
      button: true,
      selected: selected,
      label: '${DateFormat('EEEE d MMMM').format(date)}${busy ? ', has classes' : ''}${today ? ', today' : ''}',
      excludeSemantics: true,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 2),
        child: Material(
          color: selected ? SsColors.navy : (today ? SsColors.blue50 : Colors.transparent),
          borderRadius: BorderRadius.circular(SsRadius.control),
          child: InkWell(
            borderRadius: BorderRadius.circular(SsRadius.control),
            onTap: onTap,
            child: Padding(
              padding: const EdgeInsets.symmetric(vertical: SsSpace.s2),
              child: Column(children: [
                Text(DateFormat('E').format(date).substring(0, 2).toUpperCase(),
                    style: TextStyle(fontFamily: 'Inter', fontSize: 11, fontWeight: FontWeight.w700, color: selected ? const Color(0xCCFFFFFF) : SsColors.textSecondary)),
                const SizedBox(height: 2),
                Text('${date.day}', style: TextStyle(fontFamily: 'PlusJakartaSans', fontSize: 17, fontWeight: FontWeight.w700, color: fg)),
                const SizedBox(height: 4),
                Container(
                  width: 6,
                  height: 6,
                  decoration: BoxDecoration(
                    shape: BoxShape.circle,
                    color: busy ? (selected ? SsColors.orange : SsColors.blue) : Colors.transparent,
                  ),
                ),
              ]),
            ),
          ),
        ),
      ),
    );
  }
}

/// One hour of the timetable: start/end time on the left, the subject card on the right.
class TimetableSlot extends StatelessWidget {
  const TimetableSlot({super.key, required this.session, required this.time});
  final SessionListItem session;
  final TimeFormatter time;

  @override
  Widget build(BuildContext context) {
    final s = session;
    final t = Theme.of(context).textTheme;
    final color = subjectColor(s.subjectName ?? s.title);
    final status = StatusStyles.session(s.status);
    final live = s.status == SessionStatus.live;
    return Padding(
      padding: const EdgeInsets.only(bottom: SsSpace.s3),
      child: IntrinsicHeight(
        child: Row(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          SizedBox(
            width: 52,
            child: ExcludeSemantics(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(time.time(s.startUtc), style: t.titleSmall?.copyWith(fontFeatures: const [FontFeature.tabularFigures()])),
                Text(time.time(s.endUtc), style: t.labelSmall?.copyWith(fontFeatures: const [FontFeature.tabularFigures()])),
              ]),
            ),
          ),
          Expanded(
            child: SsCard(
              onTap: () => context.push('/sessions/${s.id}'),
              accent: color,
              padding: const EdgeInsets.all(SsSpace.s3),
              semanticLabel: '${time.range(s.startUtc, s.endUtc)}, ${s.subjectName ?? s.title}, ${s.teacherName}, ${status.label}',
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  SizedBox.square(dimension: 44, child: ClassCoverImage(subject: s.subjectName, title: s.title, radius: 12, shade: false)),
                  const SizedBox(width: SsSpace.s3),
                  Expanded(
                    child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                      Text(s.subjectName ?? s.title,
                          style: t.titleMedium?.copyWith(decoration: status.strikeThrough ? TextDecoration.lineThrough : null), maxLines: 1, overflow: TextOverflow.ellipsis),
                      Row(children: [
                        UserAvatar(name: s.teacherName, photoUrl: s.teacherPhotoUrl, size: 18, ring: false),
                        const SizedBox(width: 6),
                        Expanded(child: Text(s.teacherName, style: t.labelSmall, maxLines: 1, overflow: TextOverflow.ellipsis)),
                      ]),
                    ]),
                  ),
                  if (!canJoin(s.status, s.hasMeetingLink) || !live) StatusBadge(status, dense: true),
                ]),
                if (canJoin(s.status, s.hasMeetingLink) && live) ...[
                  const SizedBox(height: SsSpace.s3),
                  JoinSessionButton(session: s, expand: true),
                ],
              ]),
            ),
          ),
        ]),
      ),
    );
  }
}
