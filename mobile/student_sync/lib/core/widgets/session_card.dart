import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../models/enums.dart';
import '../models/session_models.dart';
import '../theme/status_styles.dart';
import '../theme/tokens.dart';
import '../time/time_format.dart';
import 'avatar.dart';
import 'class_cover.dart';
import 'common.dart';

/// Session summary: cover photo with the date, title and status, time in the viewer's
/// zone (+ organiser zone when different), the teacher, teaching-mode label, and an
/// optional action (e.g. Join).
class SessionCard extends StatelessWidget {
  const SessionCard({
    super.key,
    required this.session,
    required this.time,
    this.onTap,
    this.action,
    this.highlight = false,
    this.showTeacher = true,
  });

  final SessionListItem session;
  final TimeFormatter time;
  final VoidCallback? onTap;
  final Widget? action;

  /// Orange accent for the "next class" card.
  final bool highlight;
  final bool showTeacher;

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).textTheme;
    final status = StatusStyles.session(session.status);
    final organiser = time.organiserRange(session.startUtc, session.endUtc, session.timeZoneId);
    final local = time.inViewerZone(session.startUtc);
    final secondary = [
      if (session.subjectName != null) session.subjectName!,
      if (session.batchName != null && session.sessionType == SessionType.batch && session.batchName != session.title) session.batchName!,
    ];
    return SsCard(
      onTap: onTap,
      accent: highlight ? SsColors.orange : null,
      padding: const EdgeInsets.all(SsSpace.s3),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
          // Cover thumbnail with the calendar date on it.
          SizedBox.square(
            dimension: 84,
            child: ClassCoverImage(
              subject: session.subjectName,
              title: session.title,
              radius: SsRadius.control,
              child: Padding(
                padding: const EdgeInsets.all(SsSpace.s2),
                child: Align(
                  alignment: Alignment.bottomLeft,
                  child: ExcludeSemantics(
                    child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
                      Text(DateFormat('EEE').format(local).toUpperCase(),
                          style: const TextStyle(fontFamily: 'Inter', fontSize: 10, fontWeight: FontWeight.w700, letterSpacing: 0.8, color: Color(0xE6FFFFFF))),
                      Text(DateFormat('d').format(local),
                          style: const TextStyle(fontFamily: 'PlusJakartaSans', fontSize: 22, height: 1.1, fontWeight: FontWeight.w700, color: SsColors.white)),
                    ]),
                  ),
                ),
              ),
            ),
          ),
          const SizedBox(width: SsSpace.s3),
          Expanded(
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Expanded(
                  child: Text(
                    session.title,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    style: t.titleMedium?.copyWith(decoration: status.strikeThrough ? TextDecoration.lineThrough : null),
                  ),
                ),
                const SizedBox(width: SsSpace.s2),
                StatusBadge(status, dense: true),
              ]),
              const SizedBox(height: SsSpace.s1),
              Row(children: [
                const ExcludeSemantics(child: Icon(LucideIcons.clock, size: 14, color: SsColors.blue)),
                const SizedBox(width: 6),
                Flexible(
                  child: Text(time.range(session.startUtc, session.endUtc),
                      style: t.bodyMedium?.copyWith(fontWeight: FontWeight.w600, color: SsColors.navy)),
                ),
              ]),
              if (organiser != null) Text('Organiser time: $organiser', style: t.bodySmall),
              const SizedBox(height: SsSpace.s2),
              if (showTeacher)
                Row(children: [
                  UserAvatar(name: session.teacherName, photoUrl: session.teacherPhotoUrl, size: 22, ring: false),
                  const SizedBox(width: SsSpace.s2),
                  Expanded(
                    child: Text([session.teacherName, ...secondary].join(' · '), style: t.bodySmall, maxLines: 1, overflow: TextOverflow.ellipsis),
                  ),
                ])
              else if (secondary.isNotEmpty)
                Text(secondary.join(' · '), style: t.bodySmall, maxLines: 1, overflow: TextOverflow.ellipsis),
            ]),
          ),
        ]),
        const SizedBox(height: SsSpace.s3),
        Wrap(spacing: SsSpace.s2, runSpacing: SsSpace.s2, crossAxisAlignment: WrapCrossAlignment.center, children: [
          TagPill(StatusStyles.sessionTypeLabel(session.sessionType)),
          TagPill(StatusStyles.deliveryModeLabel(session.deliveryMode),
              icon: session.deliveryMode == DeliveryMode.inPerson ? LucideIcons.mapPin : LucideIcons.video),
          if (session.isRecurring) const TagPill('Recurring', icon: LucideIcons.repeat),
        ]),
        if (action != null) ...[const SizedBox(height: SsSpace.s3), action!],
      ]),
    );
  }
}

/// Photo card for a horizontal "Coming up" carousel.
class ClassCarouselCard extends StatelessWidget {
  const ClassCarouselCard({super.key, required this.session, required this.time, this.onTap, this.showTeacher = true});
  final SessionListItem session;
  final TimeFormatter time;
  final VoidCallback? onTap;
  final bool showTeacher;

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).textTheme;
    final status = StatusStyles.session(session.status);
    return SizedBox(
      width: 252,
      child: SsCard(
        onTap: onTap,
        padding: const EdgeInsets.all(SsSpace.s2),
        semanticLabel: '${session.title}, ${time.range(session.startUtc, session.endUtc)}, ${session.teacherName}',
        child: ExcludeSemantics(
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            ClassCoverImage(
              subject: session.subjectName,
              title: session.title,
              height: 128,
              radius: SsRadius.control,
              child: Padding(
                padding: const EdgeInsets.all(SsSpace.s2),
                child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  Row(children: [
                    Flexible(child: GlassPill(icon: LucideIcons.calendar, label: time.dayLabel(session.startUtc, DateTime.now().toUtc()))),
                    // On a photo only the unusual states are called out (glass keeps them readable).
                    if (session.status != SessionStatus.scheduled) ...[const SizedBox(width: SsSpace.s2), GlassPill(label: status.label)],
                  ]),
                  const Spacer(),
                  Text(
                    session.title,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(fontFamily: 'PlusJakartaSans', fontSize: 16, height: 1.25, fontWeight: FontWeight.w700, color: SsColors.white),
                  ),
                ]),
              ),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(SsSpace.s1, SsSpace.s3, SsSpace.s1, SsSpace.s1),
              child: Row(children: [
                if (showTeacher) ...[
                  UserAvatar(name: session.teacherName, photoUrl: session.teacherPhotoUrl, size: 28, ring: false),
                  const SizedBox(width: SsSpace.s2),
                ],
                Expanded(
                  child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                    Text(showTeacher ? session.teacherName : (session.subjectName ?? StatusStyles.sessionTypeLabel(session.sessionType)),
                        style: t.labelMedium?.copyWith(fontWeight: FontWeight.w600, color: SsColors.textPrimary), maxLines: 1, overflow: TextOverflow.ellipsis),
                    Text(time.range(session.startUtc, session.endUtc), style: t.labelSmall, maxLines: 1, overflow: TextOverflow.ellipsis),
                  ]),
                ),
              ]),
            ),
          ]),
        ),
      ),
    );
  }
}

/// The next class on each role's home: its cover photo with the title over it, the
/// teacher, the time, and the Join action when the class can be joined.
class NextClassHero extends StatelessWidget {
  const NextClassHero({super.key, required this.session, required this.time, this.onTap, this.action, this.showTeacher = true});
  final SessionListItem? session;
  final TimeFormatter time;
  final VoidCallback? onTap;
  final Widget? action;
  final bool showTeacher;

  @override
  Widget build(BuildContext context) {
    final s = session;
    if (s == null) {
      return HeroCard(
        child: Row(children: [
          const IconTile(LucideIcons.calendarCheck, color: SsColors.white, background: Color(0x26FFFFFF), size: 48),
          const SizedBox(width: SsSpace.s4),
          Expanded(
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: const [
              Text('No classes scheduled', style: TextStyle(fontFamily: 'PlusJakartaSans', fontSize: 18, fontWeight: FontWeight.w700, color: SsColors.white)),
              SizedBox(height: 2),
              Text('New classes will appear here.', style: TextStyle(fontFamily: 'Inter', fontSize: 14, color: Color(0xD9FFFFFF))),
            ]),
          ),
        ]),
      );
    }
    final t = Theme.of(context).textTheme;
    final now = DateTime.now().toUtc();
    final live = s.status == SessionStatus.live;
    final organiser = time.organiserRange(s.startUtc, s.endUtc, s.timeZoneId);
    // A day label rather than a countdown, so the hero never shows a stale "starts in".
    final eyebrow = live ? 'Happening now' : time.dayLabel(s.startUtc, now);

    return SsCard(
      onTap: onTap,
      padding: const EdgeInsets.all(SsSpace.s2),
      semanticLabel: 'Next class: ${s.title}, ${time.range(s.startUtc, s.endUtc)}, ${s.teacherName}',
      child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        ExcludeSemantics(
          child: ClassCoverImage(
            subject: s.subjectName,
            title: s.title,
            height: 196,
            radius: SsRadius.card - 4,
            child: Padding(
              padding: const EdgeInsets.all(SsSpace.s4),
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Row(children: [
                  GlassPill(
                    child: Row(mainAxisSize: MainAxisSize.min, children: [
                      Container(width: 7, height: 7, decoration: const BoxDecoration(color: SsColors.orange, shape: BoxShape.circle)),
                      const SizedBox(width: 6),
                      Text(live ? 'LIVE' : 'NEXT CLASS',
                          style: const TextStyle(fontFamily: 'Inter', fontSize: 11, fontWeight: FontWeight.w700, letterSpacing: 1, color: SsColors.white)),
                    ]),
                  ),
                  const Spacer(),
                  GlassPill(icon: LucideIcons.calendar, label: eyebrow),
                ]),
                const Spacer(),
                Text(
                  s.title,
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: const TextStyle(fontFamily: 'PlusJakartaSans', fontSize: 22, height: 1.2, fontWeight: FontWeight.w700, letterSpacing: -0.4, color: SsColors.white),
                ),
                if (s.subjectName != null) ...[
                  const SizedBox(height: 2),
                  Text(s.subjectName!, style: const TextStyle(fontFamily: 'Inter', fontSize: 13, fontWeight: FontWeight.w500, color: Color(0xE6FFFFFF))),
                ],
              ]),
            ),
          ),
        ),
        Padding(
          padding: const EdgeInsets.fromLTRB(SsSpace.s2, SsSpace.s3, SsSpace.s2, SsSpace.s2),
          child: Row(children: [
            if (showTeacher) ...[
              UserAvatar(name: s.teacherName, photoUrl: s.teacherPhotoUrl, size: 40, ring: false),
              const SizedBox(width: SsSpace.s3),
              Expanded(
                child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  Text(s.teacherName, style: t.titleSmall, maxLines: 1, overflow: TextOverflow.ellipsis),
                  Text('Your teacher', style: t.labelSmall),
                ]),
              ),
            ] else
              Expanded(child: Text(StatusStyles.sessionTypeLabel(s.sessionType), style: t.titleSmall)),
            Column(crossAxisAlignment: CrossAxisAlignment.end, children: [
              Row(mainAxisSize: MainAxisSize.min, children: [
                const Icon(LucideIcons.clock, size: 14, color: SsColors.blue),
                const SizedBox(width: 4),
                Text(time.time(s.startUtc), style: t.titleSmall?.copyWith(fontFeatures: const [FontFeature.tabularFigures()])),
              ]),
              Text(StatusStyles.deliveryModeLabel(s.deliveryMode), style: t.labelSmall),
            ]),
          ]),
        ),
        if (organiser != null)
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: SsSpace.s2),
            child: Text('Organiser time: $organiser', style: t.bodySmall),
          ),
        if (action != null) Padding(padding: const EdgeInsets.all(SsSpace.s2), child: action!),
      ]),
    );
  }
}

/// Translucent pill for text and icons on a photo or a [HeroCard].
class GlassPill extends StatelessWidget {
  const GlassPill({super.key, this.icon, this.label, this.child});
  final IconData? icon;
  final String? label;
  final Widget? child;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
        decoration: BoxDecoration(
          color: const Color(0x330B2854),
          borderRadius: BorderRadius.circular(SsRadius.pill),
          border: Border.all(color: const Color(0x40FFFFFF)),
        ),
        child: child ??
            Row(mainAxisSize: MainAxisSize.min, children: [
              if (icon != null) ...[ExcludeSemantics(child: Icon(icon, size: 13, color: SsColors.white)), const SizedBox(width: 5)],
              Flexible(
                child: Text(label ?? '',
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(fontFamily: 'Inter', fontSize: 12, fontWeight: FontWeight.w600, color: SsColors.white)),
              ),
            ]),
      );
}
