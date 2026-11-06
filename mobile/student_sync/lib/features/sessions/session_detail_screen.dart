import 'package:flutter/material.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/app_services.dart';
import '../../core/models/enums.dart';
import '../../core/models/session_models.dart';
import '../../core/theme/status_styles.dart';
import '../../core/theme/tokens.dart';
import '../../core/widgets/async_view.dart';
import '../../core/widgets/avatar.dart';
import '../../core/widgets/class_cover.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/session_card.dart';
import '../videos/video_widgets.dart';
import 'attendance_tab.dart';
import 'content_tabs.dart';
import 'join_button.dart';
import 'preparation_tab.dart';
import 'teacher_tools.dart';

/// Everything about one class on a single page: the class itself (with Join), then its
/// videos, notes, materials, recordings and attendance — and, for the teacher who runs
/// it, class preparation. Sections with nothing in them are left out.
///
/// [initialTab] (`?tab=attendance`, `notes`, `materials`, `recordings`, `videos`,
/// `preparation`) scrolls to that section once it has loaded.
class SessionDetailScreen extends StatelessWidget {
  const SessionDetailScreen({super.key, required this.sessionId, this.initialTab});
  final String sessionId;
  final String? initialTab;

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    return Scaffold(
      appBar: AppBar(title: const Text('Class details')),
      body: AsyncView<SessionDetail>(
        load: () => services.sessions.detail(sessionId),
        builder: (context, s, reload) => _ClassPage(session: s, reload: reload, initialSection: initialTab),
        loadingLabel: 'Loading class',
      ),
    );
  }
}

class _ClassPage extends StatefulWidget {
  const _ClassPage({required this.session, required this.reload, this.initialSection});
  final SessionDetail session;
  final Future<void> Function() reload;
  final String? initialSection;

  @override
  State<_ClassPage> createState() => _ClassPageState();
}

class _ClassPageState extends State<_ClassPage> {
  final _anchors = <String, GlobalKey>{};
  int _version = 0;
  bool _scrolled = false;

  GlobalKey _anchor(String id) => _anchors.putIfAbsent(id, GlobalKey.new);

  /// Scrolls to the requested section the first time it appears.
  VoidCallback? _onLoaded(String id) => widget.initialSection != id
      ? null
      : () {
          if (_scrolled || !mounted) return;
          final target = _anchors[id]?.currentContext;
          if (target == null) return;
          _scrolled = true;
          Scrollable.ensureVisible(target, duration: SsMotion.medium, curve: SsMotion.curve);
        };

  Future<void> _refresh() async {
    setState(() => _version++);
    await widget.reload();
  }

  Widget _section(String id, Widget Function(Key key, VoidCallback? onLoaded) build) =>
      KeyedSubtree(key: _anchor(id), child: build(ValueKey('$id/$_version'), _onLoaded(id)));

  @override
  Widget build(BuildContext context) {
    final s = widget.session;
    final runsClass = s.canManage || s.canMarkAttendance;
    final videos = _section('videos', (k, l) => SessionVideosSection(key: k, sessionId: s.id, onLoaded: l));
    final notes = _section('notes', (k, l) => NotesSection(key: k, sessionId: s.id, canManage: s.canManage, onLoaded: l));
    final assignments = _section('assignments', (k, l) => AssignmentsSection(key: k, sessionId: s.id, forTeacher: s.canManage, onLoaded: l));
    final materials = _section('materials', (k, l) => MaterialsSection(key: k, sessionId: s.id, onLoaded: l));
    final recordings = _section('recordings', (k, l) => RecordingsSection(key: k, sessionId: s.id, onLoaded: l));
    final attendance = _section('attendance', (k, l) => AttendanceSection(key: k, sessionId: s.id, onLoaded: l));

    return RefreshIndicator(
      onRefresh: _refresh,
      child: ListView(padding: pagePadding(context), children: [
        const SizedBox(height: SsSpace.s2),
        _ClassHero(session: s),
        const SizedBox(height: SsSpace.s4),
        _ClassFacts(session: s),
        if (s.canManage) ...[const SizedBox(height: SsSpace.s4), TeacherToolsCard(session: s, onAdded: _refresh)],
        if (s.objectives != null && s.objectives!.isNotEmpty) ...[
          const SectionHeader('What this class covers', icon: LucideIcons.target, iconColor: SsColors.orangeDeep),
          SsCard(child: MarkdownView(s.objectives!)),
        ],
        // Teachers see what needs doing first; students see what they can watch and read.
        // Teachers see what needs doing first; students see the class's notes and
        // assignments (added after the class), then its videos and the rest.
        if (runsClass) ...[
          if (s.canManage) _section('preparation', (k, l) => PreparationSection(key: k, sessionId: s.id, onLoaded: l)),
          attendance,
          notes,
          assignments,
          videos,
          recordings,
          materials,
        ] else ...[
          notes,
          assignments,
          videos,
          recordings,
          materials,
          attendance,
        ],
        const SizedBox(height: SsSpace.s5),
        Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
          const ExcludeSemantics(child: Icon(LucideIcons.info, size: 16, color: SsColors.slate)),
          const SizedBox(width: SsSpace.s2),
          Expanded(
            child: Text(
              'Notes and assignments are added after the class; videos unlock around it. Pull down to refresh.',
              style: Theme.of(context).textTheme.bodySmall,
            ),
          ),
        ]),
      ]),
    );
  }
}

/// Cover photo header (status and day over the photo), then the title, the teacher with
/// their photo, the key facts as pills, and the Join button.
class _ClassHero extends StatelessWidget {
  const _ClassHero({required this.session});
  final SessionDetail session;

  static String _duration(Duration d) => d.inMinutes >= 60 && d.inMinutes % 60 == 0 ? '${d.inHours} h' : '${d.inMinutes} min';

  @override
  Widget build(BuildContext context) {
    final s = session;
    final t = Theme.of(context).textTheme;
    final time = AppScope.of(context).time;
    final status = StatusStyles.session(s.status);
    final joinable = canJoin(s.status, s.meetingUrl != null && s.meetingUrl!.isNotEmpty);
    return Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
      ExcludeSemantics(
        child: DecoratedBox(
          decoration: BoxDecoration(borderRadius: BorderRadius.circular(SsRadius.card + 4), boxShadow: SsShadow.floating),
          child: ClassCoverImage(
            subject: s.subjectName,
            title: s.title,
            height: 210,
            radius: SsRadius.card + 4,
            child: Padding(
              padding: const EdgeInsets.all(SsSpace.s4),
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Row(children: [
                  GlassPill(
                    child: Row(mainAxisSize: MainAxisSize.min, children: [
                      Container(
                        width: 7,
                        height: 7,
                        decoration: BoxDecoration(color: s.status == SessionStatus.live ? SsColors.orange : SsColors.white, shape: BoxShape.circle),
                      ),
                      const SizedBox(width: 6),
                      Text(status.label.toUpperCase(),
                          style: const TextStyle(fontFamily: 'Inter', fontSize: 11, fontWeight: FontWeight.w700, letterSpacing: 1, color: SsColors.white)),
                    ]),
                  ),
                  const Spacer(),
                  GlassPill(icon: LucideIcons.calendar, label: time.dayLabel(s.startUtc, DateTime.now().toUtc())),
                ]),
                const Spacer(),
                if (s.subjectName != null)
                  Text(s.subjectName!.toUpperCase(),
                      style: const TextStyle(fontFamily: 'Inter', fontSize: 12, fontWeight: FontWeight.w700, letterSpacing: 1.2, color: Color(0xE6FFFFFF))),
              ]),
            ),
          ),
        ),
      ),
      const SizedBox(height: SsSpace.s4),
      Semantics(
        header: true,
        child: Text(
          s.title,
          style: t.headlineMedium?.copyWith(decoration: status.strikeThrough ? TextDecoration.lineThrough : null),
        ),
      ),
      const SizedBox(height: SsSpace.s3),
      Row(children: [
        UserAvatar(name: s.teacherName, photoUrl: s.teacherPhotoUrl, size: 44, ring: false),
        const SizedBox(width: SsSpace.s3),
        Expanded(
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text(s.teacherName, style: t.titleSmall),
            Text('Teacher', style: t.labelSmall),
          ]),
        ),
      ]),
      const SizedBox(height: SsSpace.s4),
      Wrap(spacing: SsSpace.s2, runSpacing: SsSpace.s2, children: [
        _FactPill(icon: LucideIcons.clock, label: time.range(s.startUtc, s.endUtc)),
        _FactPill(icon: LucideIcons.timer, label: _duration(s.endUtc.difference(s.startUtc))),
        _FactPill(
          icon: s.deliveryMode == DeliveryMode.inPerson ? LucideIcons.mapPin : LucideIcons.video,
          label: StatusStyles.deliveryModeLabel(s.deliveryMode),
        ),
        _FactPill(icon: LucideIcons.usersRound, label: StatusStyles.sessionTypeLabel(s.sessionType)),
      ]),
      if (joinable) ...[
        const SizedBox(height: SsSpace.s4),
        FilledButton.icon(
          style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(54), shape: const StadiumBorder()),
          onPressed: () => openExternal(context, s.meetingUrl!),
          icon: const Icon(LucideIcons.video, size: 20),
          label: Text(s.status == SessionStatus.live ? 'Join live class' : 'Join class'),
        ),
      ],
    ]);
  }
}

/// White pill with an icon, for the class facts under the title.
class _FactPill extends StatelessWidget {
  const _FactPill({required this.icon, required this.label});
  final IconData icon;
  final String label;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.symmetric(horizontal: SsSpace.s3, vertical: SsSpace.s2),
        decoration: BoxDecoration(
          color: SsColors.white,
          borderRadius: BorderRadius.circular(SsRadius.pill),
          border: Border.all(color: SsColors.hairline),
          boxShadow: SsShadow.card,
        ),
        child: Row(mainAxisSize: MainAxisSize.min, children: [
          ExcludeSemantics(child: Icon(icon, size: 15, color: SsColors.navy)),
          const SizedBox(width: 6),
          Flexible(child: Text(label, style: const TextStyle(fontFamily: 'Inter', fontSize: 13, fontWeight: FontWeight.w600, color: SsColors.textPrimary))),
        ]),
      );
}

/// The practical details, each on its own row.
class _ClassFacts extends StatelessWidget {
  const _ClassFacts({required this.session});
  final SessionDetail session;

  static String _duration(Duration d) => d.inMinutes >= 60 && d.inMinutes % 60 == 0 ? '${d.inHours} h' : '${d.inMinutes} min';

  @override
  Widget build(BuildContext context) {
    final s = session;
    final time = AppScope.of(context).time;
    final t = Theme.of(context).textTheme;
    final organiser = time.organiserRange(s.startUtc, s.endUtc, s.timeZoneId);
    return Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
      if (s.status == SessionStatus.cancelled && s.cancellationReason != null) ...[
        SsCard(
          accent: SsColors.error,
          child: Text('Cancelled: ${s.cancellationReason}', style: t.bodyMedium),
        ),
        const SizedBox(height: SsSpace.s3),
      ],
      SsCard(
        child: Column(children: [
          InfoRow(
            icon: LucideIcons.clock,
            label: 'When (your time, ${time.viewerZoneId})',
            value: '${time.range(s.startUtc, s.endUtc)} · ${_duration(s.endUtc.difference(s.startUtc))}',
          ),
          if (organiser != null) InfoRow(icon: LucideIcons.globe, label: "Organiser's time", value: organiser),
          InfoRow(icon: LucideIcons.user, label: 'Teacher', value: s.teacherName),
          if (s.subjectName != null)
            InfoRow(
              icon: LucideIcons.bookOpen,
              label: 'Subject',
              value: [s.subjectName!, if (s.chapterTitle != null) s.chapterTitle!].join(' · '),
            ),
          if (s.batchName != null) InfoRow(icon: LucideIcons.usersRound, label: 'Batch', value: s.batchName!),
          if (s.arrangementTitle != null) InfoRow(icon: LucideIcons.handshake, label: 'Arrangement', value: s.arrangementTitle!),
          if (s.location != null && s.location!.isNotEmpty) InfoRow(icon: LucideIcons.mapPin, label: 'Location', value: s.location!),
          if (s.meetingUrl == null && s.deliveryMode != DeliveryMode.inPerson && s.status != SessionStatus.cancelled)
            const InfoRow(icon: LucideIcons.unlink, label: 'Meeting link', value: 'Not added yet'),
          if (s.canManage || s.canMarkAttendance) InfoRow(icon: LucideIcons.graduationCap, label: 'Students', value: '${s.participantCount}'),
        ]),
      ),
    ]);
  }
}
