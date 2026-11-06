import 'common.dart';
import 'enums.dart';
import 'json.dart';

class Note {
  const Note({
    required this.id,
    required this.sessionId,
    required this.title,
    required this.bodyMarkdown,
    required this.visibility,
    required this.status,
    required this.source,
    this.publishedAtUtc,
    required this.updatedAtUtc,
    required this.authorName,
    required this.availableOffline,
  });

  final String id;
  final String sessionId;
  final String title;
  final String bodyMarkdown;
  final NoteVisibility visibility;
  final PublicationStatus status;
  final ContentSource source;
  final DateTime? publishedAtUtc;
  final DateTime updatedAtUtc;
  final String authorName;
  final bool availableOffline;

  factory Note.fromJson(Json j) => Note(
        id: j.str('id'),
        sessionId: j.str('sessionId'),
        title: j.str('title'),
        bodyMarkdown: j.str('bodyMarkdown'),
        visibility: j.enumValue('visibility', NoteVisibility.values),
        status: j.enumValue('status', PublicationStatus.values),
        source: j.enumValue('source', ContentSource.values),
        publishedAtUtc: j.dateOrNull('publishedAtUtc'),
        updatedAtUtc: j.date('updatedAtUtc'),
        authorName: j.str('authorName'),
        availableOffline: j.boolean('availableOffline'),
      );
}

class LearningMaterial {
  const LearningMaterial({
    required this.id,
    required this.title,
    this.description,
    required this.kind,
    this.url,
    this.file,
    this.subjectName,
    required this.createdAtUtc,
  });

  final String id;
  final String title;
  final String? description;
  final MaterialKind kind;
  final String? url;
  final FileDto? file;
  final String? subjectName;
  final DateTime createdAtUtc;

  factory LearningMaterial.fromJson(Json j) => LearningMaterial(
        id: j.str('id'),
        title: j.str('title'),
        description: j.strOrNull('description'),
        kind: j.enumValue('kind', MaterialKind.values),
        url: j.strOrNull('url'),
        file: j.objOrNull('file') == null ? null : FileDto.fromJson(j.obj('file')),
        subjectName: j.strOrNull('subjectName'),
        createdAtUtc: j.date('createdAtUtc'),
      );
}

class Recording {
  const Recording({
    required this.id,
    required this.sessionId,
    required this.title,
    required this.status,
    this.durationSeconds,
    required this.createdAtUtc,
    required this.retainUntilUtc,
    this.resumePositionSeconds,
    required this.completed,
    this.fileId,
  });

  final String id;
  final String sessionId;
  final String title;
  final RecordingStatus status;
  final int? durationSeconds;
  final DateTime createdAtUtc;
  final DateTime retainUntilUtc;
  final int? resumePositionSeconds;
  final bool completed;

  /// Stored media file; null until processing has produced a playable file.
  final String? fileId;

  /// Playback needs both a file and an Available status.
  bool get isPlayable => fileId != null && status == RecordingStatus.available;

  factory Recording.fromJson(Json j) => Recording(
        id: j.str('id'),
        sessionId: j.str('sessionId'),
        title: j.str('title'),
        status: j.enumValue('status', RecordingStatus.values),
        durationSeconds: j.intOrNull('durationSeconds'),
        createdAtUtc: j.date('createdAtUtc'),
        retainUntilUtc: j.date('retainUntilUtc'),
        resumePositionSeconds: j.intOrNull('resumePositionSeconds'),
        completed: j.boolean('completed'),
        fileId: j.strOrNull('fileId'),
      );
}

/// A pre-recorded class video. The server decides the lock state; [fileId] is only
/// sent when the viewer may play it (unlocked, or the viewer manages the class).
class PrerecordedVideo {
  const PrerecordedVideo({
    required this.id,
    required this.sessionId,
    required this.sessionTitle,
    required this.sessionStartUtc,
    required this.sessionEndUtc,
    required this.sessionCancelled,
    required this.title,
    this.description,
    this.durationSeconds,
    required this.unlockRule,
    required this.unlockOffsetMinutes,
    required this.accessOverride,
    required this.isUnlocked,
    this.unlocksAtUtc,
    this.fileId,
    required this.canManage,
    required this.createdAtUtc,
    required this.rowVersion,
  });

  final String id;
  final String sessionId;
  final String sessionTitle;
  final DateTime sessionStartUtc;
  final DateTime sessionEndUtc;
  final bool sessionCancelled;
  final String title;
  final String? description;
  final int? durationSeconds;
  final VideoUnlockRule unlockRule;
  final int unlockOffsetMinutes;
  final VideoAccessOverride accessOverride;
  final bool isUnlocked;

  /// When the automatic rule will unlock it; null once unlocked or when it never will.
  final DateTime? unlocksAtUtc;
  final String? fileId;
  final bool canManage;
  final DateTime createdAtUtc;
  final String rowVersion;

  bool get isPlayable => fileId != null;

  factory PrerecordedVideo.fromJson(Json j) => PrerecordedVideo(
        id: j.str('id'),
        sessionId: j.str('sessionId'),
        sessionTitle: j.str('sessionTitle'),
        sessionStartUtc: j.date('sessionStartUtc'),
        sessionEndUtc: j.date('sessionEndUtc'),
        sessionCancelled: j.boolean('sessionCancelled'),
        title: j.str('title'),
        description: j.strOrNull('description'),
        durationSeconds: j.intOrNull('durationSeconds'),
        unlockRule: j.enumValue('unlockRule', VideoUnlockRule.values),
        unlockOffsetMinutes: j.integer('unlockOffsetMinutes'),
        accessOverride: j.enumValue('accessOverride', VideoAccessOverride.values),
        isUnlocked: j.boolean('isUnlocked'),
        unlocksAtUtc: j.dateOrNull('unlocksAtUtc'),
        fileId: j.strOrNull('fileId'),
        canManage: j.boolean('canManage'),
        createdAtUtc: j.date('createdAtUtc'),
        rowVersion: j.str('rowVersion'),
      );
}

class RecentContent {
  const RecentContent({required this.kind, required this.id, required this.sessionId, required this.title, required this.sessionTitle, required this.atUtc});
  final String kind;
  final String id;
  final String sessionId;
  final String title;
  final String sessionTitle;
  final DateTime atUtc;

  factory RecentContent.fromJson(Json j) => RecentContent(
        kind: j.str('kind'),
        id: j.str('id'),
        sessionId: j.str('sessionId'),
        title: j.str('title'),
        sessionTitle: j.str('sessionTitle'),
        atUtc: j.date('atUtc'),
      );
}

class AppNotification {
  const AppNotification({required this.id, required this.type, required this.title, required this.body, this.link, required this.createdAtUtc, required this.isRead});
  final String id;

  /// Kept as the raw server string so new notification types never break the list.
  final String type;
  final String title;
  final String body;
  final String? link;
  final DateTime createdAtUtc;
  final bool isRead;

  factory AppNotification.fromJson(Json j) => AppNotification(
        id: j.str('id'),
        type: j.str('type'),
        title: j.str('title'),
        body: j.str('body'),
        link: j.strOrNull('link'),
        createdAtUtc: j.date('createdAtUtc'),
        isRead: j.boolean('isRead'),
      );
}

class UnreadCounts {
  const UnreadCounts({required this.notifications, required this.messages});
  final int notifications;
  final int messages;
  factory UnreadCounts.fromJson(Json j) => UnreadCounts(notifications: j.integer('notifications'), messages: j.integer('messages'));
}
