import 'content_models.dart';
import 'json.dart';
import 'session_models.dart';

class SubjectTeacher {
  const SubjectTeacher({required this.id, required this.name, this.photoUrl});
  final String id;
  final String name;
  final String? photoUrl;

  factory SubjectTeacher.fromJson(Json j) => SubjectTeacher(id: j.str('id'), name: j.str('name'), photoUrl: j.strOrNull('photoUrl'));
}

/// Progress counters shared by the subject list and the syllabus.
mixin SubjectProgress {
  int get chapterCount;
  int get completedChapterCount;
  int get classCount;
  int get completedClassCount;

  /// Share of the syllabus covered so far (0..1), by chapters, falling back to classes.
  double get progress => chapterCount > 0
      ? completedChapterCount / chapterCount
      : classCount > 0
          ? completedClassCount / classCount
          : 0;
}

/// A subject the user has classes in (`GET /my/subjects`).
class MySubject with SubjectProgress {
  const MySubject({
    required this.subjectId,
    required this.name,
    required this.code,
    required this.courseName,
    required this.teachers,
    required this.chapterCount,
    required this.completedChapterCount,
    required this.classCount,
    required this.completedClassCount,
    required this.videoCount,
    required this.unlockedVideoCount,
    this.nextSession,
    this.kind = SubjectKind.core,
    this.group,
  });

  final String subjectId;
  final String name;
  final String code;
  final String courseName;
  final List<SubjectTeacher> teachers;
  final SubjectKind kind;

  /// The choice group for a language (e.g. "Second language").
  final String? group;
  @override
  final int chapterCount;
  @override
  final int completedChapterCount;
  @override
  final int classCount;
  @override
  final int completedClassCount;
  final int videoCount;
  final int unlockedVideoCount;
  final SessionListItem? nextSession;

  factory MySubject.fromJson(Json j) => MySubject(
        subjectId: j.str('subjectId'),
        name: j.str('name'),
        code: j.str('code'),
        courseName: j.str('courseName'),
        teachers: j.list('teachers', SubjectTeacher.fromJson),
        chapterCount: j.integer('chapterCount'),
        completedChapterCount: j.integer('completedChapterCount'),
        classCount: j.integer('classCount'),
        completedClassCount: j.integer('completedClassCount'),
        videoCount: j.integer('videoCount'),
        unlockedVideoCount: j.integer('unlockedVideoCount'),
        nextSession: j.objOrNull('nextSession') == null ? null : SessionListItem.fromJson(j.obj('nextSession')),
        kind: parseSubjectKind(j.strOrNull('kind')),
        group: j.strOrNull('group'),
      );
}

/// Every student in a standard has the core subjects; languages are chosen per student;
/// additional subjects are added by the admin for particular students.
enum SubjectKind { core, language, additional }

SubjectKind parseSubjectKind(String? s) => switch (s) {
      'language' => SubjectKind.language,
      'additional' => SubjectKind.additional,
      _ => SubjectKind.core,
    };

enum ChapterStatus { completed, inProgress, upcoming, notScheduled }

ChapterStatus parseChapterStatus(String s) => switch (s) {
      'completed' => ChapterStatus.completed,
      'inProgress' => ChapterStatus.inProgress,
      'upcoming' => ChapterStatus.upcoming,
      _ => ChapterStatus.notScheduled,
    };

class SyllabusChapter {
  const SyllabusChapter({
    this.chapterId,
    required this.title,
    required this.sortOrder,
    required this.status,
    required this.sessions,
    required this.videos,
    required this.recordings,
  });

  /// Null for the "Other classes" group (classes not linked to a chapter).
  final String? chapterId;
  final String title;
  final int sortOrder;
  final ChapterStatus status;
  final List<SessionListItem> sessions;
  final List<PrerecordedVideo> videos;
  final List<Recording> recordings;

  int get watchableCount => videos.length + recordings.length;

  factory SyllabusChapter.fromJson(Json j) => SyllabusChapter(
        chapterId: j.strOrNull('chapterId'),
        title: j.str('title'),
        sortOrder: j.integer('sortOrder'),
        status: parseChapterStatus(j.str('status')),
        sessions: j.list('sessions', SessionListItem.fromJson),
        videos: j.list('videos', PrerecordedVideo.fromJson),
        recordings: j.list('recordings', Recording.fromJson),
      );
}

/// One subject's syllabus with its classes and videos (`GET /my/subjects/{id}`).
class SubjectSyllabus with SubjectProgress {
  const SubjectSyllabus({
    required this.subjectId,
    required this.name,
    required this.code,
    required this.courseName,
    this.description,
    required this.teachers,
    required this.chapterCount,
    required this.completedChapterCount,
    required this.classCount,
    required this.completedClassCount,
    required this.videoCount,
    required this.unlockedVideoCount,
    this.nextSession,
    required this.chapters,
    this.kind = SubjectKind.core,
    this.group,
  });

  final String subjectId;
  final String name;
  final String code;
  final String courseName;
  final String? description;
  final List<SubjectTeacher> teachers;
  @override
  final int chapterCount;
  @override
  final int completedChapterCount;
  @override
  final int classCount;
  @override
  final int completedClassCount;
  final int videoCount;
  final int unlockedVideoCount;
  final SessionListItem? nextSession;
  final List<SyllabusChapter> chapters;
  final SubjectKind kind;
  final String? group;

  factory SubjectSyllabus.fromJson(Json j) => SubjectSyllabus(
        subjectId: j.str('subjectId'),
        name: j.str('name'),
        code: j.str('code'),
        courseName: j.str('courseName'),
        description: j.strOrNull('description'),
        teachers: j.list('teachers', SubjectTeacher.fromJson),
        chapterCount: j.integer('chapterCount'),
        completedChapterCount: j.integer('completedChapterCount'),
        classCount: j.integer('classCount'),
        completedClassCount: j.integer('completedClassCount'),
        videoCount: j.integer('videoCount'),
        unlockedVideoCount: j.integer('unlockedVideoCount'),
        nextSession: j.objOrNull('nextSession') == null ? null : SessionListItem.fromJson(j.obj('nextSession')),
        chapters: j.list('chapters', SyllabusChapter.fromJson),
        kind: parseSubjectKind(j.strOrNull('kind')),
        group: j.strOrNull('group'),
      );
}
