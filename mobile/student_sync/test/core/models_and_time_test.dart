import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:student_sync/core/auth/auth_controller.dart';
import 'package:student_sync/core/auth/welcome_store.dart';
import 'package:student_sync/core/models/content_models.dart';
import 'package:student_sync/core/models/dashboard_models.dart';
import 'package:student_sync/core/models/doubt_models.dart';
import 'package:student_sync/core/models/enums.dart';
import 'package:student_sync/core/models/json.dart';
import 'package:student_sync/core/models/learning_models.dart';
import 'package:student_sync/core/models/session_models.dart';
import 'package:student_sync/core/models/subject_models.dart';
import 'package:student_sync/core/routing/router.dart';
import 'package:student_sync/core/time/time_format.dart';
import 'package:student_sync/core/widgets/class_cover.dart';
import 'package:student_sync/features/sessions/join_button.dart' show joinableNow;
import 'package:student_sync/features/sessions/timetable_screen.dart' show subjectColor;
import 'package:student_sync/features/learning/assignment_detail_screen.dart';
import 'package:student_sync/features/notifications/notifications_screen.dart';
import 'package:student_sync/features/sessions/recording_player_screen.dart';
import 'package:timezone/data/latest_all.dart' as tzdata;

import '../support/fixtures.dart';

/// Round-trips through JSON text so tests see exactly what `jsonDecode` produces.
Json decoded(Object value) => jsonDecode(jsonEncode(value)) as Json;

void main() {
  setUpAll(tzdata.initializeTimeZones);

  group('Model parsing (contract-shaped JSON)', () {
    test('student dashboard parses nested sessions, assignments, doubts and progress', () {
      final d = StudentDashboard.fromJson(decoded(studentDashboardJson));
      expect(d.firstName, 'Test');
      expect(d.nextSession!.sessionType, SessionType.oneToOne);
      expect(d.nextSession!.batchId, isNull, reason: 'one-to-one sessions have no batch');
      expect(d.nextSession!.startUtc, DateTime.utc(2026, 11, 7, 6));
      expect(d.nextSession!.startUtc.isUtc, isTrue);
      expect(d.dueAssignments.single.mySubmissionStatus, SubmissionStatus.graded);
      expect(d.dueAssignments.single.myMarks, 8.5);
      expect(d.dueAssignments.single.dueAtUtc, DateTime.utc(2026, 10, 28, 19, 13, 19, 864, 881), reason: '7-digit .NET fractions are accepted');
      expect(d.openDoubts.single.status, DoubtStatus.newDoubt, reason: '"New" maps to newDoubt');
      expect(d.progress.all, hasLength(7));
      expect(d.progress.averageExamScore.percent, isNull);
      expect(d.progress.attendance.howCalculated, isNotEmpty);
      expect(d.notifications.single.isRead, isFalse);
    });

    test('attempt questions parse every type and saved responses', () {
      final a = Attempt.fromJson(decoded(attemptJson));
      expect(a.questions.map((q) => q.type), [
        QuestionType.singleChoice,
        QuestionType.multipleChoice,
        QuestionType.trueFalse,
        QuestionType.numerical,
        QuestionType.fillBlank,
        QuestionType.matching,
        QuestionType.longAnswer,
      ]);
      expect(a.questions[1].savedResponse!.optionIds, ['o2000000-0000-0000-0000-000000000001']);
      expect(a.questions[3].savedResponse!.number, 10);
      expect(a.questions[5].matchChoices, hasLength(2));
      expect(a.questions[5].savedResponse!.matches, {'o6000000-0000-0000-0000-000000000002': 'Turning point'});
      expect(a.deadlineUtc.difference(a.startedAtUtc), const Duration(minutes: 30));
    });

    test('ExamResponse serialises to the server shape', () {
      const r = ExamResponse(matches: {'a': 'b'});
      expect(r.toJson(), {'optionIds': null, 'text': null, 'number': null, 'matches': {'a': 'b'}});
      expect(const ExamResponse(text: '  ').isEmpty, isTrue);
      expect(const ExamResponse(number: 0).isEmpty, isFalse);
    });

    test('doubt detail, session detail and attendance roster parse', () {
      final doubt = DoubtDetail.fromJson(decoded({
        'id': 'd1', 'title': 'T', 'description': 'D', 'status': 'PendingConfirmation', 'priority': 'High', 'isPrivate': true,
        'studentId': 's1', 'studentName': 'S', 'subjectId': null, 'subjectName': null, 'chapterId': null, 'sessionId': null, 'sessionTitle': null,
        'recordingId': null, 'recordingTimestampSeconds': null, 'assignedTeacherId': null, 'assignedTeacherName': null,
        'followUpSessionId': null, 'followUpSessionTitle': null, 'followUpSessionStartUtc': null,
        'messages': [
          {'id': 'm1', 'authorName': 'Teacher', 'isTeacherReply': true, 'body': 'Answer', 'createdAtUtc': '2026-10-31T19:13:21Z', 'attachments': []}
        ],
        'history': [
          {'atUtc': '2026-10-31T19:13:21Z', 'from': 'New', 'to': 'Answered', 'changedBy': 'Teacher', 'note': null}
        ],
        'attachments': [], 'canAnswer': false, 'canConfirm': true, 'rowVersion': 'AAAAAAAACK0=',
      }));
      expect(doubt.status, DoubtStatus.pendingConfirmation);
      expect(doubt.priority, DoubtPriority.high);
      expect(doubt.history.single.from, DoubtStatus.newDoubt);
      expect(doubt.messages.single.isTeacherReply, isTrue);

      final roster = AttendanceRoster.fromJson(decoded({
        'sessionId': 's', 'sessionStatus': 'Completed', 'canEdit': true,
        'rows': [
          {'studentId': 'a', 'studentName': 'A', 'status': 'Late', 'note': null, 'markedAtUtc': '2026-10-28T16:00:00Z'},
          {'studentId': 'b', 'studentName': 'B', 'status': null, 'note': null, 'markedAtUtc': null},
        ],
      }));
      expect(roster.rows.first.status, AttendanceStatus.late);
      expect(roster.rows.last.status, isNull);
      expect(const AttendanceEntry(studentId: 'a', status: AttendanceStatus.excused).toJson(), {'studentId': 'a', 'status': 'Excused', 'note': null});
    });

    test('recording parses the trailing fileId; Play needs a file and Available status', () {
      Json rec({Object? fileId, String status = 'Available'}) => decoded({
            'id': 'r1', 'sessionId': 's1', 'sessionTitle': 'Algebra', 'title': 'Lesson 4', 'status': status, 'durationSeconds': 3600,
            'createdAtUtc': '2026-10-30T10:00:00Z', 'retainUntilUtc': '2027-10-30T10:00:00Z', 'resumePositionSeconds': 754,
            'completed': false, 'fileId': fileId,
          });
      final playable = Recording.fromJson(rec(fileId: 'f1111111-1111-1111-1111-111111111111'));
      expect(playable.fileId, 'f1111111-1111-1111-1111-111111111111');
      expect(playable.resumePositionSeconds, 754);
      expect(playable.isPlayable, isTrue);
      expect(Recording.fromJson(rec()).fileId, isNull);
      expect(Recording.fromJson(rec()).isPlayable, isFalse, reason: 'no file yet: Play is hidden');
      expect(Recording.fromJson(rec(fileId: 'f1', status: 'Pending')).isPlayable, isFalse);
      expect(Recording.fromJson(decoded(rec()..remove('fileId'))).fileId, isNull, reason: 'older servers omit the field');
    });

    test('exam list item parses latestAttemptId; View result only without an in-progress attempt', () {
      Json exam({Object? latestAttemptId, bool inProgress = false}) => decoded({
            'id': 'e1', 'title': 'Quadratics quiz', 'status': 'Published', 'opensAtUtc': '2026-10-01T00:00:00Z', 'closesAtUtc': '2026-10-02T00:00:00Z',
            'durationMinutes': 30, 'questionCount': 7, 'totalMarks': 20, 'subjectName': 'Maths', 'resultsReleased': true, 'attemptsUsed': 1,
            'maxAttempts': 1, 'myBestScore': 15.5, 'hasInProgressAttempt': inProgress, 'latestAttemptId': latestAttemptId,
          });
      final done = ExamListItem.fromJson(exam(latestAttemptId: 'a1111111-1111-1111-1111-111111111111'));
      expect(done.latestAttemptId, 'a1111111-1111-1111-1111-111111111111');
      expect(done.canViewResult, isTrue);
      expect(ExamListItem.fromJson(exam(latestAttemptId: 'a1', inProgress: true)).canViewResult, isFalse);
      expect(ExamListItem.fromJson(exam()).latestAttemptId, isNull);
      expect(ExamListItem.fromJson(exam()).canViewResult, isFalse);
    });

    test('contract drift fails loudly instead of producing wrong data', () {
      final broken = decoded(sessionListItemJson)..['sessionType'] = 'Webinar';
      expect(() => SessionListItem.fromJson(broken), throwsFormatException);
      final missing = decoded(sessionListItemJson)..remove('title');
      expect(() => SessionListItem.fromJson(missing), throwsFormatException);
    });

    test('timestamps without an offset are treated as UTC, never device-local', () {
      expect(parseUtc('2026-11-07T06:00:00'), DateTime.utc(2026, 11, 7, 6));
      expect(parseUtc('2026-11-07T10:00:00+04:00'), DateTime.utc(2026, 11, 7, 6));
    });
  });

  group('TimeFormatter', () {
    test('shows times in the viewer zone and adds the organiser zone when different', () {
      final dubai = TimeFormatter('Asia/Dubai');
      final start = DateTime.utc(2026, 11, 7, 6);
      final end = DateTime.utc(2026, 11, 7, 7);
      expect(dubai.range(start, end), 'Sat 7 Nov · 10:00–11:00');
      expect(dubai.organiserRange(start, end, 'Asia/Dubai'), isNull);

      final london = TimeFormatter('Europe/London');
      expect(london.range(start, end), 'Sat 7 Nov · 06:00–07:00');
      expect(london.organiserRange(start, end, 'Asia/Dubai'), '10:00–11:00 Asia/Dubai');
    });

    test('handles daylight saving: the same UTC hour is a different London wall time across the change', () {
      final london = TimeFormatter('Europe/London');
      // BST ends 25 Oct 2026 (UTC+1 → UTC+0).
      expect(london.time(DateTime.utc(2026, 10, 24, 15)), '16:00');
      expect(london.time(DateTime.utc(2026, 10, 31, 15)), '15:00');
      expect(TimeFormatter.offsetLabel('Europe/London', DateTime.utc(2026, 7, 1)), 'UTC+01:00');
      expect(TimeFormatter.offsetLabel('America/St_Johns', DateTime.utc(2026, 1, 1)), 'UTC-03:30');
    });

    test('day grouping uses the viewer zone, not UTC', () {
      final tokyo = TimeFormatter('Asia/Tokyo');
      // 20:00 UTC on 6 Nov is already 7 Nov in Tokyo.
      expect(tokyo.dayKey(DateTime.utc(2026, 11, 6, 20)), '2026-11-07');
      expect(tokyo.dayLabel(DateTime.utc(2026, 11, 6, 20), DateTime.utc(2026, 11, 6, 1)), 'Tomorrow');
    });

    test('an unknown zone id falls back to UTC instead of the device offset', () {
      final f = TimeFormatter('Not/AZone');
      expect(f.time(DateTime.utc(2026, 1, 1, 9, 5)), '09:05');
      expect(TimeFormatter.isKnownZone('Not/AZone'), isFalse);
    });

    test('format helpers', () {
      expect(formatMarks(8.0), '8');
      expect(formatMarks(7.5), '7.5');
      expect(formatCountdown(const Duration(minutes: 29, seconds: 5)), '29:05');
      expect(formatCountdown(const Duration(hours: 1, minutes: 2, seconds: 3)), '1:02:03');
      expect(formatCountdown(const Duration(seconds: -4)), '00:00');
    });
  });

  group('Routing rules', () {
    test('redirects by auth state and role', () {
      expect(authRedirect(status: AuthStatus.initializing, role: null, mustChangePassword: false, location: '/login'), '/splash');
      expect(authRedirect(status: AuthStatus.signedOut, role: null, mustChangePassword: false, location: '/student/home'), '/login');
      expect(authRedirect(status: AuthStatus.signedIn, role: UserRole.student, mustChangePassword: true, location: '/student/home'), '/change-password');
      expect(authRedirect(status: AuthStatus.signedIn, role: UserRole.student, mustChangePassword: false, location: '/login'), '/student/home');
      expect(authRedirect(status: AuthStatus.signedIn, role: UserRole.parent, mustChangePassword: false, location: '/teacher/tasks'), '/parent/home');
      expect(authRedirect(status: AuthStatus.signedIn, role: UserRole.teacher, mustChangePassword: false, location: '/sessions/abc'), isNull);
      expect(authRedirect(status: AuthStatus.signedIn, role: UserRole.institutionAdmin, mustChangePassword: false, location: '/student/home'), '/web-portal');
    });

    test('notification links map to role-appropriate screens', () {
      const id = '8661d6f1-e1dd-46f5-7488-08df3783074c';
      expect(inAppRouteFor('/doubts/$id', UserRole.student), '/doubts/$id');
      expect(inAppRouteFor('/assignments/$id', UserRole.student), '/assignments/$id');
      expect(inAppRouteFor('/assignments/$id', UserRole.teacher), '/teacher/tasks');
      expect(inAppRouteFor('/doubts/$id', UserRole.parent), isNull);
      expect(inAppRouteFor('/sessions/$id', UserRole.parent), '/sessions/$id');
      expect(inAppRouteFor('https://evil.example/doubts/not-a-guid', UserRole.student), isNull);
      expect(inAppRouteFor(null, UserRole.student), isNull);
    });
  });

  group('Submission rules', () {
    AssignmentDetail assignment({bool late = false, bool resubmit = false, String status = 'Published'}) => AssignmentDetail.fromJson(decoded({
          'id': 'a', 'title': 'T', 'instructionsMarkdown': '', 'status': status, 'dueAtUtc': '2026-11-01T10:00:00Z', 'maxMarks': 10,
          'allowLateSubmission': late, 'allowResubmission': resubmit, 'teacherId': 't', 'teacherName': 'T', 'sessionId': null,
          'sessionTitle': null, 'batchId': null, 'subjectId': null, 'attachments': [], 'recipients': [], 'canManage': false, 'rowVersion': 'x',
        }));
    Submission submission(String status) => Submission.fromJson(decoded({
          'id': 's', 'assignmentId': 'a', 'assignmentTitle': 'T', 'studentId': 'st', 'studentName': 'S', 'status': status, 'textAnswer': 'x',
          'submittedAtUtc': null, 'isLate': false, 'attemptNumber': 1, 'marks': null, 'maxMarks': 10, 'feedback': null, 'gradedAtUtc': null,
          'files': [], 'rowVersion': 'y',
        }));
    final before = DateTime.utc(2026, 11, 1, 9);
    final after = DateTime.utc(2026, 11, 1, 11);

    test('editable before the deadline, locked after unless late submissions are allowed', () {
      expect(submissionEditability(assignment(), null, before).editable, isTrue);
      expect(submissionEditability(assignment(), null, after).editable, isFalse);
      expect(submissionEditability(assignment(late: true), null, after).editable, isTrue);
    });

    test('graded and non-resubmittable submissions are locked; returned ones reopen', () {
      expect(submissionEditability(assignment(), submission('Graded'), before).editable, isFalse);
      expect(submissionEditability(assignment(), submission('Submitted'), before).editable, isFalse);
      expect(submissionEditability(assignment(resubmit: true), submission('Submitted'), before).editable, isTrue);
      expect(submissionEditability(assignment(), submission('ReturnedForCorrection'), after).editable, isTrue);
      expect(submissionEditability(assignment(status: 'Closed'), null, before).editable, isFalse);
    });
  });

  // The reporter returns its pending send; these tests record sends synchronously.
  // ignore_for_file: unawaited_futures
  group('Recording playback progress', () {
    test('reports on pause and on finish, never twice for the same position', () async {
      final sent = <(int, bool)>[];
      final r = RecordingProgressReporter((p, c) async => sent.add((p, c)));
      r.onState(isPlaying: false, position: const Duration(seconds: 754), isCompleted: false); // initial seek, not a pause
      r.onState(isPlaying: true, position: const Duration(seconds: 760), isCompleted: false);
      r.onState(isPlaying: false, position: const Duration(seconds: 800), isCompleted: false); // pause
      r.onState(isPlaying: false, position: const Duration(seconds: 800), isCompleted: false);
      r.onState(isPlaying: true, position: const Duration(seconds: 800), isCompleted: false);
      r.onState(isPlaying: false, position: const Duration(seconds: 3600), isCompleted: true); // finished
      r.flush();
      expect(sent, [(800, false), (3600, true)]);
    });

    test('closing without playing sends nothing; closing mid-play sends the position', () {
      final sent = <(int, bool)>[];
      RecordingProgressReporter((p, c) async => sent.add((p, c))).flush();
      expect(sent, isEmpty);
      final r = RecordingProgressReporter((p, c) async => sent.add((p, c)));
      r.onState(isPlaying: true, position: const Duration(seconds: 42), isCompleted: false);
      r.flush();
      expect(sent, [(42, false)]);
    });

    test('resume hint uses the #t media fragment', () {
      final u = Uri.parse('http://localhost:5080/api/v1/files/download?token=abc');
      expect(withResumeHint(u, 754).toString(), 'http://localhost:5080/api/v1/files/download?token=abc#t=754');
      expect(withResumeHint(u, null), u);
      expect(withResumeHint(u, 0), u);
    });
  });

  group('Welcome screens', () {
    test('signed-out visitors see the welcome screens once, then sign-in', () {
      expect(authRedirect(status: AuthStatus.signedOut, role: null, mustChangePassword: false, location: '/login', seenWelcome: false), '/welcome');
      expect(authRedirect(status: AuthStatus.signedOut, role: null, mustChangePassword: false, location: '/welcome', seenWelcome: false), isNull);
      expect(authRedirect(status: AuthStatus.signedOut, role: null, mustChangePassword: false, location: '/welcome'), isNull);
      expect(authRedirect(status: AuthStatus.signedOut, role: null, mustChangePassword: false, location: '/student/home'), '/login');
      expect(authRedirect(status: AuthStatus.signedIn, role: UserRole.student, mustChangePassword: false, location: '/welcome', seenWelcome: false), '/student/home');
    });

    test('completing the welcome screens is remembered', () async {
      final store = InMemoryWelcomeStore(initiallySeen: false);
      final state = WelcomeState(store);
      await state.load();
      expect(state.value, isFalse);
      await state.complete();
      expect(state.value, isTrue);
      expect(await store.seen(), isTrue);
    });
  });

  test('class covers follow the subject, then the title, then a general photo', () {
    expect(ClassCovers.assetFor('Mathematics', 'G10 Maths'), 'assets/images/covers/maths.jpg');
    expect(ClassCovers.assetFor('Chemistry', 'Chemistry 1:1'), 'assets/images/covers/chemistry.jpg');
    expect(ClassCovers.assetFor(null, 'IELTS Writing 1:1'), 'assets/images/covers/english.jpg');
    expect(ClassCovers.assetFor('Physics', 'Revision clinic'), 'assets/images/covers/physics.jpg');
    expect(ClassCovers.assetFor(null, 'Parent meeting'), 'assets/images/covers/study.jpg');
    expect(ClassCovers.assetFor('Physics', 'Grade 10 Science & Mathematics'), 'assets/images/covers/physics.jpg', reason: 'subject wins over course name');
    expect(ClassCovers.assetFor('Social Science', 'Class 10 (CBSE)'), 'assets/images/covers/social.jpg', reason: 'not the science photo');
    expect(ClassCovers.assetFor('Science', 'Class 10 (CBSE)'), 'assets/images/covers/science.jpg');
    expect(ClassCovers.assetFor('Accountancy', 'Class 11 Commerce (CBSE)'), 'assets/images/covers/commerce.jpg');
    expect(ClassCovers.assetFor('Applied Mathematics', 'Class 11 Commerce (CBSE)'), 'assets/images/covers/maths.jpg');
    expect(ClassCovers.assetFor('Hindi', 'Class 10 (CBSE)'), 'assets/images/covers/languages.jpg');
    expect(ClassCovers.assetFor('English Core', 'Class 12 Science (CBSE)'), 'assets/images/covers/english.jpg');
    expect(ClassCovers.assetFor('The World Around Us', 'Class 4 (CBSE)'), 'assets/images/covers/science.jpg');
  });

  test('subject syllabus parses chapters in order, with the "Other classes" group and progress', () {
    final syllabus = SubjectSyllabus.fromJson({
      'subjectId': 's1', 'name': 'Mathematics', 'code': 'MATH10', 'courseName': 'Grade 10 Science & Mathematics', 'description': null,
      'teachers': [{'id': 't1', 'name': 'Amira Haddad (Fictional)', 'photoUrl': '/api/v1/files/download?token=x&v=abc'}],
      'chapterCount': 4, 'completedChapterCount': 1, 'classCount': 6, 'completedClassCount': 2, 'videoCount': 2, 'unlockedVideoCount': 1,
      'nextSession': sessionListItemJson,
      'chapters': [
        {'chapterId': 'c1', 'title': 'Algebra basics', 'sortOrder': 1, 'status': 'completed', 'sessions': [sessionListItemJson], 'videos': [], 'recordings': []},
        {'chapterId': 'c2', 'title': 'Quadratic equations', 'sortOrder': 2, 'status': 'inProgress', 'sessions': [], 'videos': [], 'recordings': []},
        {'chapterId': 'c3', 'title': 'Trigonometry', 'sortOrder': 3, 'status': 'notScheduled', 'sessions': [], 'videos': [], 'recordings': []},
        {'chapterId': null, 'title': 'Other classes', 'sortOrder': 2147483647, 'status': 'upcoming', 'sessions': [], 'videos': [], 'recordings': []},
      ],
    });
    expect(syllabus.chapters.map((c) => c.status), [ChapterStatus.completed, ChapterStatus.inProgress, ChapterStatus.notScheduled, ChapterStatus.upcoming]);
    expect(syllabus.chapters.last.chapterId, isNull);
    expect(syllabus.chapters.first.sessions.single.id, sessionListItemJson['id']);
    expect(syllabus.teachers.single.photoUrl, isNotNull);
    expect(syllabus.nextSession, isNotNull);
    expect(syllabus.progress, 0.25);
    expect(parseChapterStatus('something-new'), ChapterStatus.notScheduled, reason: 'unknown values degrade safely');
  });

  test('subjects are grouped core / language / additional; unknown kinds count as core', () {
    expect(parseSubjectKind('language'), SubjectKind.language);
    expect(parseSubjectKind('additional'), SubjectKind.additional);
    expect(parseSubjectKind(null), SubjectKind.core);
    final s = MySubject.fromJson({
      'subjectId': 'h', 'name': 'Hindi', 'code': 'C10-L2-HIN', 'courseName': 'Class 10 (CBSE)', 'teachers': [],
      'chapterCount': 0, 'completedChapterCount': 0, 'classCount': 0, 'completedClassCount': 0, 'videoCount': 0, 'unlockedVideoCount': 0,
      'nextSession': null, 'kind': 'language', 'group': 'Second language',
    });
    expect(s.kind, SubjectKind.language);
    expect(s.group, 'Second language');
    expect(s.progress, 0, reason: 'a subject with no classes yet has no progress');
  });

  test('each subject keeps the same timetable colour', () {
    expect(subjectColor('Mathematics'), subjectColor('mathematics'));
    expect(subjectColor('English'), isNot(subjectColor('')));
  });

  test('Join is offered from 15 minutes before a class until it ends', () {
    final start = DateTime.utc(2026, 11, 7, 10);
    SessionListItem at(String status) => SessionListItem.fromJson({
          ...sessionListItemJson,
          'status': status,
          'hasMeetingLink': true,
          'startUtc': '2026-11-07T10:00:00Z',
          'endUtc': '2026-11-07T11:00:00Z',
        });
    expect(joinableNow(at('Scheduled'), start.subtract(const Duration(days: 1))), isFalse, reason: 'not a day ahead');
    expect(joinableNow(at('Scheduled'), start.subtract(const Duration(minutes: 15))), isTrue);
    expect(joinableNow(at('Scheduled'), start.add(const Duration(minutes: 59))), isTrue);
    expect(joinableNow(at('Scheduled'), start.add(const Duration(hours: 1))), isFalse, reason: 'over');
    expect(joinableNow(at('Live'), start.subtract(const Duration(hours: 2))), isTrue, reason: 'live is always joinable');
    expect(joinableNow(at('Cancelled'), start), isFalse);
  });
}
