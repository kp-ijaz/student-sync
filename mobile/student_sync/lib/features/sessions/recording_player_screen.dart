import 'dart:async';

import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';
import 'package:video_player/video_player.dart';

import '../../core/app_services.dart';
import '../../core/models/common.dart';
import '../../core/models/content_models.dart';
import '../../core/models/doubt_models.dart';
import '../../core/models/enums.dart';
import '../../core/theme/tokens.dart';
import '../../core/time/time_format.dart';
import '../../core/widgets/async_view.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/states.dart';
import '../doubts/doubt_card.dart';
import '../videos/video_widgets.dart';

/// Adds the `#t=<seconds>` media-fragment resume hint. Browsers honour it; the
/// in-app player ignores fragments, so it seeks explicitly instead.
Uri withResumeHint(Uri uri, int? resumeSeconds) =>
    resumeSeconds == null || resumeSeconds <= 0 ? uri : uri.replace(fragment: 't=$resumeSeconds');

/// Decides when a student's watch position is reported: when playback stops
/// (pause or finish) and when the player is closed mid-play. Duplicate reports of
/// the same position are skipped.
class RecordingProgressReporter {
  RecordingProgressReporter(this._send);
  final Future<void> Function(int positionSeconds, bool completed) _send;

  bool _wasPlaying = false;
  bool _played = false;
  bool _sentCompleted = false;
  int _position = 0;
  bool _completed = false;
  (int, bool)? _lastSent;

  /// Feed every player state change. Returns the pending report, if one was sent.
  Future<void>? onState({required bool isPlaying, required Duration position, required bool isCompleted}) {
    _position = position.inSeconds;
    _completed = _completed || isCompleted;
    _played = _played || isPlaying;
    final paused = _wasPlaying && !isPlaying;
    final finished = isCompleted && !_sentCompleted;
    _wasPlaying = isPlaying;
    if (!paused && !finished) return null;
    return _report();
  }

  /// Call when the player closes; reports only if the recording was played and the
  /// position changed since the last report.
  Future<void>? flush() => _played ? _report() : null;

  Future<void>? _report() {
    final next = (_position, _completed);
    if (next == _lastSent) return null;
    _lastSent = next;
    if (_completed) _sentCompleted = true;
    return _send(_position, _completed);
  }
}

/// What the player needs. Watch position is only tracked for live-class recordings
/// ([recordingId] set); pre-recorded videos simply play.
class PlayableMedia {
  const PlayableMedia({
    required this.title,
    required this.fileId,
    this.recordingId,
    this.videoId,
    this.sessionId,
    this.sessionTitle,
    this.sessionStartUtc,
    this.description,
    this.resumePositionSeconds,
    this.durationSeconds,
    this.completed = false,
  });

  factory PlayableMedia.recording(Recording r) => PlayableMedia(
        title: r.title,
        fileId: r.fileId!,
        recordingId: r.id,
        sessionId: r.sessionId,
        resumePositionSeconds: r.resumePositionSeconds,
        durationSeconds: r.durationSeconds,
        completed: r.completed,
      );

  factory PlayableMedia.video(PrerecordedVideo v) => PlayableMedia(
        title: v.title,
        fileId: v.fileId!,
        videoId: v.id,
        sessionId: v.sessionId,
        sessionTitle: v.sessionTitle,
        sessionStartUtc: v.sessionStartUtc,
        description: v.description,
        durationSeconds: v.durationSeconds,
      );

  final String title;
  final String fileId;
  final String? recordingId;
  final String? videoId;

  /// The class the media belongs to; the page lists its questions and other videos.
  final String? sessionId;
  final String? sessionTitle;
  final DateTime? sessionStartUtc;
  final String? description;
  final int? resumePositionSeconds;
  final int? durationSeconds;
  final bool completed;
}

/// Streams a recording or pre-recorded video in-app. Links are short-lived and
/// user-bound, so a fresh one is fetched for each load or retry (and the server
/// refuses one for a video that has been locked again).
class RecordingPlayerScreen extends StatefulWidget {
  const RecordingPlayerScreen({super.key, required this.media});
  final PlayableMedia media;

  @override
  State<RecordingPlayerScreen> createState() => _RecordingPlayerScreenState();
}

class _RecordingPlayerScreenState extends State<RecordingPlayerScreen> {
  VideoPlayerController? _controller;
  Object? _error;
  Uri? _lastUri;
  RecordingProgressReporter? _reporter;
  bool _progressErrorShown = false;
  late final AppServices _services;
  late final bool _isStudent;

  @override
  void initState() {
    super.initState();
    _services = AppScope.read(context);
    _isStudent = _services.auth.user?.role == UserRole.student;
    final recordingId = widget.media.recordingId;
    if (_isStudent && recordingId != null) {
      _reporter = RecordingProgressReporter((position, completed) => _services.sessions
          .saveRecordingProgress(recordingId, positionSeconds: position, completed: completed)
          .catchError((Object e) {
        // Playback continues; tell the student once that their place wasn't saved.
        if (mounted && !_progressErrorShown) {
          _progressErrorShown = true;
          showSnack(context, "Couldn't save your place in this recording.", error: true);
        }
      }));
    }
    _load(startAt: _resumeAt(widget.media));
  }

  static Duration? _resumeAt(PlayableMedia r) {
    final s = r.resumePositionSeconds;
    if (s == null || s <= 0 || r.completed) return null;
    if (r.durationSeconds != null && s >= r.durationSeconds! - 5) return null;
    return Duration(seconds: s);
  }

  Future<void> _load({Duration? startAt}) async {
    final old = _controller;
    old?.removeListener(_onTick);
    setState(() {
      _controller = null;
      _error = null;
    });
    await old?.dispose();
    try {
      final link = await _services.sessions.fileLink(widget.media.fileId);
      final uri = _services.config.resolveServerPath(link.url);
      _lastUri = uri;
      final controller = VideoPlayerController.networkUrl(uri);
      await controller.initialize();
      if (startAt != null && startAt < controller.value.duration) await controller.seekTo(startAt);
      if (!mounted) {
        await controller.dispose();
        return;
      }
      controller.addListener(_onTick);
      setState(() => _controller = controller);
      await controller.play();
    } on Object catch (e) {
      if (mounted) setState(() => _error = e);
    }
  }

  void _onTick() {
    final c = _controller;
    if (c == null) return;
    final v = c.value;
    if (v.hasError && _error == null) {
      setState(() => _error = v.errorDescription ?? 'Playback failed.');
      return;
    }
    _reporter?.onState(isPlaying: v.isPlaying, position: v.position, isCompleted: v.isCompleted);
  }

  Future<void> _openExternally() async {
    final uri = _lastUri;
    if (uri == null) return;
    final hint = _isStudent ? _controller?.value.position.inSeconds ?? widget.media.resumePositionSeconds : null;
    await openExternal(context, withResumeHint(uri, hint).toString(), inApp: true);
  }

  @override
  void dispose() {
    final c = _controller;
    if (c != null) {
      c.removeListener(_onTick);
      _reporter?.onState(isPlaying: false, position: c.value.position, isCompleted: c.value.isCompleted);
      c.dispose();
    }
    _reporter?.flush();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final media = widget.media;
    final player = _error != null
        ? _PlayerError(error: _error!, onRetry: () => _load(startAt: _controller?.value.position), onOpenExternally: _lastUri == null ? null : _openExternally)
        : _controller == null
            ? const _PlayerLoading()
            : _PlayerCard(controller: _controller!);
    final details = <Widget>[
      _AboutCard(media: media),
      if (media.sessionId != null) ...[
        _ClassQuestions(sessionId: media.sessionId!, canAsk: _isStudent),
        _MoreVideos(sessionId: media.sessionId!, excludeVideoId: media.videoId),
      ],
    ];
    return Scaffold(
      appBar: AppBar(title: Text(media.title)),
      body: SafeArea(
        child: LayoutBuilder(builder: (context, c) {
          final padding = pagePadding(context).copyWith(top: SsSpace.s4);
          // Tablets and landscape: player on the left, details alongside.
          if (c.maxWidth >= 840) {
            return Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Expanded(flex: 3, child: SingleChildScrollView(padding: padding.copyWith(right: SsSpace.s2), child: player)),
              Expanded(flex: 2, child: ListView(padding: padding.copyWith(left: SsSpace.s2), children: details)),
            ]);
          }
          return ListView(padding: padding, children: [player, const SizedBox(height: SsSpace.s4), ...details]);
        }),
      ),
    );
  }
}

/// Video with its controls in one card: tap the picture to play/pause, scrub, skip
/// 10 s either way, and change the speed.
class _PlayerCard extends StatelessWidget {
  const _PlayerCard({required this.controller});
  final VideoPlayerController controller;

  static const _speeds = [0.75, 1.0, 1.25, 1.5, 2.0];

  void _toggle(VideoPlayerValue v) {
    if (v.isPlaying) {
      controller.pause();
    } else {
      if (v.isCompleted) controller.seekTo(Duration.zero);
      controller.play();
    }
  }

  void _skip(VideoPlayerValue v, int seconds) {
    final target = v.position + Duration(seconds: seconds);
    controller.seekTo(target < Duration.zero ? Duration.zero : (target > v.duration ? v.duration : target));
  }

  static String _speedLabel(double s) => '${formatMarks(s)}×';

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).textTheme;
    return SsCard(
      padding: EdgeInsets.zero,
      child: ValueListenableBuilder<VideoPlayerValue>(
        valueListenable: controller,
        builder: (context, v, _) => Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          ClipRRect(
            borderRadius: const BorderRadius.vertical(top: Radius.circular(SsRadius.card)),
            child: ColoredBox(
              color: Colors.black,
              child: AspectRatio(
                aspectRatio: v.aspectRatio > 0 ? v.aspectRatio : 16 / 9,
                child: Semantics(
                  button: true,
                  label: v.isPlaying ? 'Pause video' : 'Play video',
                  excludeSemantics: true,
                  child: GestureDetector(
                    onTap: () => _toggle(v),
                    child: Stack(fit: StackFit.expand, children: [
                      VideoPlayer(controller),
                      if (v.isBuffering)
                        const Center(child: CircularProgressIndicator(color: SsColors.white))
                      else if (!v.isPlaying)
                        Center(
                          child: Container(
                            width: 64,
                            height: 64,
                            decoration: BoxDecoration(color: Colors.black.withValues(alpha: 0.55), shape: BoxShape.circle),
                            child: Icon(v.isCompleted ? LucideIcons.rotateCcw : LucideIcons.play, size: 40, color: SsColors.white),
                          ),
                        ),
                    ]),
                  ),
                ),
              ),
            ),
          ),
          Padding(
            padding: const EdgeInsets.fromLTRB(SsSpace.s3, SsSpace.s2, SsSpace.s3, SsSpace.s2),
            child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
              VideoProgressIndicator(
                controller,
                allowScrubbing: true,
                padding: const EdgeInsets.symmetric(vertical: SsSpace.s2),
                colors: const VideoProgressColors(playedColor: SsColors.blue, bufferedColor: SsColors.border, backgroundColor: SsColors.blue50),
              ),
              Row(children: [
                IconButton(tooltip: 'Back 10 seconds', icon: const Icon(LucideIcons.rotateCcw), onPressed: () => _skip(v, -10)),
                IconButton.filled(tooltip: v.isPlaying ? 'Pause' : 'Play', icon: Icon(v.isPlaying ? LucideIcons.pause : LucideIcons.play), onPressed: () => _toggle(v)),
                IconButton(tooltip: 'Forward 10 seconds', icon: const Icon(LucideIcons.rotateCw), onPressed: () => _skip(v, 10)),
                const SizedBox(width: SsSpace.s1),
                Expanded(
                  child: FittedBox(
                    alignment: Alignment.centerLeft,
                    fit: BoxFit.scaleDown,
                    child: Text(
                      '${formatCountdown(v.position)} / ${formatCountdown(v.duration)}',
                      style: t.bodySmall?.copyWith(fontFeatures: const [FontFeature.tabularFigures()]),
                    ),
                  ),
                ),
                PopupMenuButton<double>(
                  tooltip: 'Playback speed',
                  initialValue: v.playbackSpeed,
                  onSelected: controller.setPlaybackSpeed,
                  itemBuilder: (_) => [
                    for (final s in _speeds) CheckedPopupMenuItem(value: s, checked: s == v.playbackSpeed, child: Text(_speedLabel(s))),
                  ],
                  child: Container(
                    constraints: const BoxConstraints(minWidth: 48, minHeight: 48),
                    alignment: Alignment.center,
                    child: Container(
                      padding: const EdgeInsets.symmetric(horizontal: SsSpace.s2, vertical: SsSpace.s1),
                      decoration: BoxDecoration(color: SsColors.blue50, borderRadius: BorderRadius.circular(SsRadius.pill)),
                      child: Text(_speedLabel(v.playbackSpeed), style: t.labelMedium?.copyWith(color: SsColors.navy)),
                    ),
                  ),
                ),
              ]),
            ]),
          ),
        ]),
      ),
    );
  }
}

class _PlayerLoading extends StatelessWidget {
  const _PlayerLoading();

  @override
  Widget build(BuildContext context) => ClipRRect(
        borderRadius: BorderRadius.circular(SsRadius.card),
        child: ColoredBox(
          color: Colors.black,
          child: AspectRatio(
            aspectRatio: 16 / 9,
            child: Center(child: Semantics(label: 'Loading video', liveRegion: true, child: const CircularProgressIndicator(color: SsColors.white))),
          ),
        ),
      );
}

class _PlayerError extends StatelessWidget {
  const _PlayerError({required this.error, required this.onRetry, this.onOpenExternally});
  final Object error;
  final VoidCallback onRetry;
  final VoidCallback? onOpenExternally;

  @override
  Widget build(BuildContext context) => SsCard(
        child: Column(children: [
          ErrorState(error: error, onRetry: onRetry),
          if (onOpenExternally != null)
            TextButton.icon(onPressed: onOpenExternally, icon: const Icon(LucideIcons.externalLink), label: const Text('Open in browser')),
        ]),
      );
}

/// Title, class, length and the teacher's description.
class _AboutCard extends StatelessWidget {
  const _AboutCard({required this.media});
  final PlayableMedia media;

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).textTheme;
    final time = AppScope.of(context).time;
    final meta = [
      if (media.sessionTitle != null) media.sessionTitle!,
      if (media.sessionStartUtc != null) time.date(media.sessionStartUtc!),
      if (media.durationSeconds != null) videoLengthLabel(media.durationSeconds!),
    ].join(' · ');
    return SsCard(
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Semantics(header: true, child: Text(media.title, style: t.titleLarge)),
        if (meta.isNotEmpty) ...[
          const SizedBox(height: SsSpace.s1),
          Text(meta, style: t.bodySmall),
        ],
        if (media.description != null && media.description!.isNotEmpty) ...[
          const SizedBox(height: SsSpace.s3),
          Text(media.description!, style: t.bodyMedium),
        ],
        if (media.sessionId != null) ...[
          const SizedBox(height: SsSpace.s2),
          TextButton.icon(
            style: TextButton.styleFrom(padding: EdgeInsets.zero, alignment: Alignment.centerLeft),
            onPressed: () => context.push('/sessions/${media.sessionId}'),
            icon: const Icon(LucideIcons.calendarRange),
            label: const Text('Open class page'),
          ),
        ],
      ]),
    );
  }
}

/// Questions (doubts) raised about this class, with a shortcut for students to ask one.
class _ClassQuestions extends StatelessWidget {
  const _ClassQuestions({required this.sessionId, required this.canAsk});
  final String sessionId;
  final bool canAsk;

  static const _shown = 5;

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    final t = Theme.of(context).textTheme;
    return Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
      const SectionHeader('Questions about this class'),
      AsyncView<PagedResult<DoubtListItem>>(
        loadingLabel: 'Loading questions',
        load: () => services.doubts.list(sessionId: sessionId, pageSize: _shown),
        builder: (context, page, reload) => Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          if (canAsk)
            SsCard(
              color: SsColors.blue50,
              child: Row(children: [
                const ExcludeSemantics(child: Icon(LucideIcons.circleQuestionMark, color: SsColors.blue)),
                const SizedBox(width: SsSpace.s3),
                Expanded(child: Text('Stuck on something in this video? Ask your teacher.', style: t.bodyMedium)),
                const SizedBox(width: SsSpace.s2),
                FilledButton(
                  onPressed: () async {
                    await context.push('/doubts/new?sessionId=$sessionId');
                    await reload();
                  },
                  child: const Text('Ask'),
                ),
              ]),
            ),
          if (page.items.isEmpty)
            Padding(
              padding: const EdgeInsets.only(top: SsSpace.s3),
              child: Text(canAsk ? 'No questions about this class yet.' : 'No student questions about this class yet.', style: t.bodySmall),
            )
          else
            for (final d in page.items)
              Padding(padding: const EdgeInsets.only(top: SsSpace.s3), child: DoubtCard(doubt: d, showStudent: !canAsk, onReturn: reload)),
          if (page.hasMore)
            Padding(
              padding: const EdgeInsets.only(top: SsSpace.s2),
              child: Text('Showing the latest $_shown. See all in Doubts.', style: t.bodySmall),
            ),
        ]),
      ),
    ]);
  }
}

/// The class's other pre-recorded videos (locked ones show when they unlock).
class _MoreVideos extends StatelessWidget {
  const _MoreVideos({required this.sessionId, this.excludeVideoId});
  final String sessionId;
  final String? excludeVideoId;

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    return AsyncView<List<PrerecordedVideo>>(
      loadingLabel: 'Loading more videos',
      load: () async => (await services.videos.forSession(sessionId)).where((v) => v.id != excludeVideoId).toList(),
      builder: (context, videos, reload) => videos.isEmpty
          ? const SizedBox.shrink()
          : Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
              const SectionHeader('More videos from this class'),
              for (final v in videos) Padding(padding: const EdgeInsets.only(bottom: SsSpace.s3), child: VideoCard(video: v, onChanged: reload)),
            ]),
    );
  }
}
