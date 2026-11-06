import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';
import 'package:student_sync/core/app_services.dart';
import 'package:student_sync/core/auth/auth_controller.dart';
import 'package:student_sync/core/auth/token_store.dart';
import 'package:student_sync/core/config/app_config.dart';
import 'package:student_sync/core/models/content_models.dart';
import 'package:student_sync/core/models/enums.dart';
import 'package:student_sync/core/models/json.dart';
import 'package:student_sync/core/models/session_models.dart';
import 'package:student_sync/core/routing/router.dart';
import 'package:student_sync/core/theme/app_theme.dart';
import 'package:student_sync/core/theme/status_styles.dart';
import 'package:student_sync/core/time/time_format.dart';
import 'package:student_sync/features/auth/login_screen.dart';
import 'package:student_sync/features/profile/progress_screen.dart';
import 'package:student_sync/features/sessions/month_calendar.dart';
import 'package:student_sync/features/sessions/recording_player_screen.dart';
import 'package:student_sync/features/sessions/sessions_list_screen.dart';
import 'package:student_sync/features/sessions/teacher_tools.dart';
import 'package:student_sync/features/videos/video_widgets.dart';
import 'package:timezone/data/latest_all.dart' as tzdata;

import '../support/fixtures.dart';

// Fictional payloads shaped like PrerecordedVideoDto / RegistrationInfoDto.
Map<String, Object?> videoJson({bool unlocked = false, bool canManage = false, String access = 'Automatic', String? unlocksAt = '2026-11-07T07:30:00Z'}) => {
      'id': '5a0c9d55-6f1e-4b0b-9d3e-0f6c2c1a7e01',
      'sessionId': sessionListItemJson['id'],
      'sessionTitle': 'Chemistry 1:1',
      'sessionStartUtc': '2026-11-07T06:00:00Z',
      'sessionEndUtc': '2026-11-07T07:00:00Z',
      'sessionCancelled': false,
      'title': 'Moles warm-up (Fictional)',
      'description': null,
      'durationSeconds': 600,
      'unlockRule': 'AfterClass',
      'unlockOffsetMinutes': 30,
      'accessOverride': access,
      'isUnlocked': unlocked,
      'unlocksAtUtc': unlocked ? null : unlocksAt,
      'fileId': unlocked || canManage ? 'f1d2c3b4-0000-4000-8000-000000000001' : null,
      'canManage': canManage,
      'createdAtUtc': '2026-11-01T10:00:00Z',
      'rowVersion': 'AAAAAAAAB9E=',
    };

Json _decoded(Object value) => jsonDecode(jsonEncode(value)) as Json;

final _teacherUser = <String, Object?>{...userJson, 'role': 'Teacher', 'fullName': 'Test Teacher (Fictional)'};

/// Services signed in as [user], with every non-auth request answered by [onApi].
Future<AppServices> _signedIn(Future<http.Response> Function(http.Request r) onApi, {Map<String, Object?> user = userJson}) async {
  final services = AppServices.create(
    config: const AppConfig(apiBaseUrl: 'https://api.example.test/api/v1', clientName: 'test'),
    httpClient: MockClient((r) async {
      if (r.url.path.endsWith('/auth/login')) {
        return http.Response(jsonEncode(authJson(access: 'a', refresh: 'r', accessExpires: DateTime.now().add(const Duration(hours: 1)), user: user)), 200);
      }
      return onApi(r);
    }),
    tokenStore: InMemoryTokenStore(),
  );
  await services.auth.restore();
  await services.auth.signIn(email: 'x@example.test', password: 'p');
  return services;
}

AppServices _anonymous(Future<http.Response> Function(http.Request r) handler) => AppServices.create(
      config: const AppConfig(apiBaseUrl: 'https://api.example.test/api/v1', clientName: 'test'),
      httpClient: MockClient(handler),
      tokenStore: InMemoryTokenStore(),
    );

Widget _host(Widget child, AppServices services) =>
    AppScope(services: services, child: MaterialApp(theme: buildAppTheme(), home: Scaffold(body: child)));

void main() {
  setUpAll(tzdata.initializeTimeZones);

  group('Pre-recorded videos', () {
    test('a locked video parses without a file and explains when it unlocks', () {
      final v = PrerecordedVideo.fromJson(_decoded(videoJson()));
      expect(v.isUnlocked, isFalse);
      expect(v.isPlayable, isFalse, reason: 'the server withholds the file of a locked video');
      expect(v.unlockRule, VideoUnlockRule.afterClass);
      expect(v.unlocksAtUtc, DateTime.utc(2026, 11, 7, 7, 30));
      expect(videoLockDetail(v, TimeFormatter('Asia/Dubai')), 'Unlocks Sat 7 Nov · 11:30');
    });

    test('rule labels match the web wording', () {
      expect(unlockRuleLabel(VideoUnlockRule.beforeClass, 0), 'Opens when the class starts');
      expect(unlockRuleLabel(VideoUnlockRule.beforeClass, 60), 'Opens 1 hour before the class');
      expect(unlockRuleLabel(VideoUnlockRule.afterClass, 30), 'Opens 30 minutes after the class');
      expect(unlockRuleLabel(VideoUnlockRule.afterClass, 2880), 'Opens 2 days after the class');
      expect(unlockOffsetLabel(10080), '1 week');
      expect(videoLengthLabel(5), '5 sec');
      expect(videoLengthLabel(600), '10 min');
      expect(StatusStyles.videoLock(isUnlocked: true, access: VideoAccessOverride.unlocked).label, 'Unlocked by teacher');
      expect(StatusStyles.videoLock(isUnlocked: false, access: VideoAccessOverride.locked).label, 'Locked by teacher');
      expect(StatusStyles.videoLock(isUnlocked: false, access: VideoAccessOverride.automatic).label, 'Locked');
    });

    testWidgets('students see a locked video without a Watch button', (tester) async {
      final services = await _signedIn((_) async => http.Response('[]', 200));
      final v = PrerecordedVideo.fromJson(_decoded(videoJson()));
      await tester.pumpWidget(_host(VideoCard(video: v, onChanged: () async {}), services));
      expect(find.text('Locked'), findsOneWidget);
      expect(find.text('Unlocks Sat 7 Nov · 11:30'), findsOneWidget);
      expect(find.text('Watch'), findsNothing);
      expect(find.byTooltip('Change who can watch'), findsNothing);
    });

    testWidgets('an unlocked video offers Watch', (tester) async {
      final services = await _signedIn((_) async => http.Response('[]', 200));
      final v = PrerecordedVideo.fromJson(_decoded(videoJson(unlocked: true)));
      await tester.pumpWidget(_host(VideoCard(video: v, onChanged: () async {}), services));
      expect(find.text('Unlocked'), findsOneWidget);
      expect(find.text('Watch'), findsOneWidget);
    });

    testWidgets('the class teacher can preview and unlock a video by hand', (tester) async {
      final requests = <http.Request>[];
      final services = await _signedIn((r) async {
        requests.add(r);
        return http.Response('', 204);
      }, user: _teacherUser);
      var changed = 0;
      final v = PrerecordedVideo.fromJson(_decoded(videoJson(canManage: true)));
      await tester.pumpWidget(_host(VideoCard(video: v, onChanged: () async => changed++), services));

      expect(find.text('Preview'), findsOneWidget, reason: 'teachers can check a video before students can');
      expect(find.text('Access: Automatic'), findsOneWidget);
      await tester.tap(find.byTooltip('Change who can watch'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Unlocked now'));
      await tester.pumpAndSettle();

      final post = requests.single;
      expect(post.method, 'POST');
      expect(post.url.path, '/api/v1/videos/${v.id}/access');
      expect(jsonDecode(post.body), {'accessOverride': 'Unlocked'});
      expect(changed, 1);
      expect(find.text('Video unlocked for students.'), findsOneWidget);
    });
  });

  group('Month calendar', () {
    test('the grid covers whole Monday-first weeks', () {
      final nov = monthGridDays(DateTime.utc(2026, 11));
      expect(nov.first, DateTime.utc(2026, 10, 26), reason: '1 Nov 2026 is a Sunday');
      expect(nov.last, DateTime.utc(2026, 12, 6));
      expect(nov, hasLength(42));
      final feb = monthGridDays(DateTime.utc(2027, 2));
      expect(feb.first, DateTime.utc(2027, 2, 1));
      expect(feb, hasLength(28));
    });

    test('classes land on the viewer\'s local day, and month bounds respect DST', () {
      final late = SessionListItem.fromJson(_decoded({...sessionListItemJson, 'startUtc': '2026-11-07T21:00:00Z', 'endUtc': '2026-11-07T22:00:00Z'}));
      expect(sessionsByDay([late], TimeFormatter('Asia/Dubai')).keys.single, DateTime.utc(2026, 11, 8));
      expect(sessionsByDay([late], TimeFormatter('UTC')).keys.single, DateTime.utc(2026, 11, 7));

      final london = TimeFormatter('Europe/London');
      expect(london.startOfViewerDayUtc(DateTime.utc(2026, 3, 29)), DateTime.utc(2026, 3, 29), reason: 'still GMT at midnight');
      expect(london.startOfViewerDayUtc(DateTime.utc(2026, 3, 30)), DateTime.utc(2026, 3, 29, 23), reason: 'BST after the change');
    });

    testWidgets('tapping a day lists that day\'s classes', (tester) async {
      final now = DateTime.now().toUtc();
      // 10:00 on the viewer's (Asia/Dubai) today — not the UTC date, which differs late in the UTC day.
      final dubai = TimeFormatter('Asia/Dubai');
      final start = dubai.startOfViewerDayUtc(dubai.viewerDate(now)).add(const Duration(hours: 10));
      Uri? requested;
      final services = await _signedIn((r) async {
        requested = r.url;
        return http.Response(
            jsonEncode([
              {...sessionListItemJson, 'startUtc': start.toIso8601String(), 'endUtc': start.add(const Duration(hours: 1)).toIso8601String()},
            ]),
            200);
      });
      await tester.pumpWidget(_host(const MonthCalendar(showTeacher: true), services));
      await tester.pumpAndSettle();

      expect(requested!.queryParameters.keys, containsAll(['fromUtc', 'toUtc', 'limit']));
      // Today (in the user's zone, Asia/Dubai) starts selected and shows its class.
      expect(find.text('Chemistry 1:1'), findsOneWidget);
      final today = services.time.viewerDate(now);
      final otherDay = today.day == 15 ? 16 : 15;
      await tester.tap(find.bySemanticsLabel(RegExp('^[A-Za-z]+ $otherDay ')).first);
      await tester.pumpAndSettle();
      expect(find.text('No classes on this day.'), findsOneWidget);
    });
  });

  group('Classes view switch', () {
    Future<void> pumpAt(WidgetTester tester, {required double width, double textScale = 1}) async {
      final services = await _signedIn((_) async => http.Response('[]', 200));
      tester.view.physicalSize = Size(width * 3, 2400);
      tester.view.devicePixelRatio = 3;
      addTearDown(tester.view.reset);
      await tester.pumpWidget(AppScope(
        services: services,
        child: MaterialApp(
          theme: buildAppTheme(),
          home: MediaQuery.withClampedTextScaling(
            minScaleFactor: textScale,
            maxScaleFactor: textScale,
            child: const SessionsListScreen(title: 'My Classes', startWithCalendar: true),
          ),
        ),
      ));
      await tester.pumpAndSettle();
    }

    void expectOneLine(WidgetTester tester, String label) {
      final paragraph = tester.renderObject<RenderParagraph>(find.text(label));
      expect(paragraph.didExceedMaxLines, isFalse, reason: '$label must not wrap');
      expect(tester.widget<Text>(find.text(label)).maxLines, 1);
    }

    testWidgets('labels stay on one line on a small phone with large text', (tester) async {
      await pumpAt(tester, width: 320, textScale: 1.6);
      for (final l in ['Calendar', 'Upcoming', 'Past']) {
        expectOneLine(tester, l);
      }
      expect(find.byIcon(LucideIcons.calendar), findsNothing, reason: 'icons are dropped when space is tight');
    });

    testWidgets('icons return when there is room', (tester) async {
      await pumpAt(tester, width: 700);
      expect(find.byIcon(LucideIcons.calendar), findsOneWidget);
      expectOneLine(tester, 'Upcoming');
    });
  });

  group('Video page', () {
    testWidgets('below the player: details, the class questions with Ask, and other videos', (tester) async {
      final requested = <Uri>[];
      final current = PrerecordedVideo.fromJson(_decoded(videoJson(unlocked: true)));
      final services = await _signedIn((r) async {
        requested.add(r.url);
        final path = r.url.path.replaceFirst('/api/v1', '');
        if (path == '/files/${current.fileId}/link') {
          return http.Response(jsonEncode({'url': '/files/download?token=t', 'expiresAtUtc': '2026-11-07T08:00:00Z'}), 200);
        }
        if (path == '/doubts') {
          return http.Response(
              jsonEncode({
                'items': [
                  {
                    'id': '8661d6f1-e1dd-46f5-7488-08df3783074c',
                    'title': 'Why is a mole 6.02 × 10²³? (Fictional)',
                    'status': 'Answered',
                    'priority': 'Normal',
                    'studentName': 'Test Student',
                    'subjectName': 'Chemistry',
                    'assignedTeacherName': 'Test Teacher',
                    'sessionId': current.sessionId,
                    'followUpSessionId': null,
                    'followUpSessionStartUtc': null,
                    'createdAtUtc': '2026-11-07T08:00:00Z',
                    'lastActivityAtUtc': '2026-11-07T08:30:00Z',
                    'replyCount': 1,
                  },
                ],
                'page': 1,
                'pageSize': 5,
                'totalCount': 1,
              }),
              200);
        }
        if (path == '/sessions/${current.sessionId}/videos') {
          return http.Response(jsonEncode([videoJson(unlocked: true), {...videoJson(), 'id': 'b1b2b3b4-0000-4000-8000-000000000002', 'title': 'Next week preview (Fictional)'}]), 200);
        }
        return http.Response('{}', 404);
      });
      tester.view.physicalSize = const Size(1170, 4000);
      tester.view.devicePixelRatio = 3;
      addTearDown(tester.view.reset);
      await tester.pumpWidget(AppScope(services: services, child: MaterialApp(theme: buildAppTheme(), home: RecordingPlayerScreen(media: PlayableMedia.video(current)))));
      await tester.pumpAndSettle();

      expect(find.text('Chemistry 1:1 · Sat 7 Nov 2026 · 10 min'), findsOneWidget);
      expect(find.text('Open class page'), findsOneWidget);
      expect(find.text('Questions about this class'), findsOneWidget);
      expect(find.text('Why is a mole 6.02 × 10²³? (Fictional)'), findsOneWidget);
      expect(find.widgetWithText(FilledButton, 'Ask'), findsOneWidget, reason: 'students can ask from the video');
      expect(requested.any((u) => u.path.endsWith('/doubts') && u.queryParameters['sessionId'] == current.sessionId), isTrue);
      expect(find.text('More videos from this class'), findsOneWidget);
      expect(find.text('Next week preview (Fictional)'), findsOneWidget);
      expect(find.text(current.title), findsWidgets);
      expect(find.text('Moles warm-up (Fictional)'), findsNWidgets(2), reason: 'app bar + details card; not repeated under "More videos"');
    });
  });

  group('Sign-in', () {
    test('signed-out visitors can only reach sign-in and welcome; there is no registration route', () {
      expect(authRedirect(status: AuthStatus.signedOut, role: null, mustChangePassword: false, location: '/login'), isNull);
      expect(authRedirect(status: AuthStatus.signedOut, role: null, mustChangePassword: false, location: '/register'), '/login');
      // A signed-in student on the old path is no longer bounced to home; the router shows its 404 screen.
      expect(authRedirect(status: AuthStatus.signedIn, role: UserRole.student, mustChangePassword: false, location: '/register'), isNull);
    });

    testWidgets('sign-in has no register button and no institution code, and says accounts come from the office', (tester) async {
      final services = _anonymous((_) async => http.Response('{}', 500));
      await services.auth.restore();
      await tester.pumpWidget(AppScope(services: services, child: MaterialApp(theme: buildAppTheme(), home: const LoginScreen())));
      expect(find.byKey(const Key('login.register')), findsNothing);
      expect(find.text('I have an institution code'), findsNothing);
      expect(find.byKey(const Key('login.institution')), findsNothing);
      expect(find.textContaining('institute office'), findsOneWidget);
    });
  });

  group('Teacher tools', () {
    SessionDetail ended({required bool over}) {
      final start = DateTime.now().toUtc().add(Duration(hours: over ? -3 : 3));
      return SessionDetail.fromJson(_decoded({
        'id': 'sess-1', 'title': 'Class 10 — Mathematics (Fictional)', 'sessionType': 'Batch', 'status': over ? 'Completed' : 'Scheduled',
        'startUtc': start.toIso8601String(), 'endUtc': start.add(const Duration(hours: 1)).toIso8601String(), 'timeZoneId': 'Asia/Dubai',
        'teacherId': 't1', 'teacherName': 'Teacher (Fictional)', 'deliveryMode': 'Online', 'meetingProvider': 'Manual',
        'canManage': true, 'canMarkAttendance': true, 'participantCount': 5, 'participants': [], 'rowVersion': 'AAAA',
      }));
    }

    testWidgets('notes and assignments open only after the class has ended; video any time', (tester) async {
      final services = await _signedIn((_) async => http.Response('{}', 200));
      await tester.pumpWidget(_host(TeacherToolsCard(session: ended(over: false), onAdded: () async {}), services));
      expect(find.textContaining('open once the class has ended'), findsOneWidget);
      await tester.tap(find.text('Note'));
      await tester.pumpAndSettle();
      expect(find.text('Add a note'), findsNothing, reason: 'disabled before the class ends');

      await tester.pumpWidget(_host(TeacherToolsCard(session: ended(over: true), onAdded: () async {}), services));
      await tester.tap(find.text('Note'));
      await tester.pumpAndSettle();
      expect(find.text('Add a note'), findsOneWidget);
    });

    testWidgets('a note is created for the class and published', (tester) async {
      final calls = <String>[];
      Map<String, dynamic>? body;
      final services = await _signedIn((r) async {
        calls.add('${r.method} ${r.url.path}');
        if (r.url.path.endsWith('/sessions/sess-1/notes')) {
          body = jsonDecode(r.body) as Map<String, dynamic>;
          return http.Response(jsonEncode({'id': 'note-1'}), 200);
        }
        return http.Response('', 204);
      });
      await tester.pumpWidget(_host(AddNoteScreen(session: ended(over: true)), services));
      await tester.tap(find.text('Publish note'));
      await tester.pump();
      expect(find.text('Enter a title.'), findsOneWidget, reason: 'validated before sending');
      await tester.enterText(find.byType(TextFormField).at(0), 'Key points (Fictional)');
      await tester.enterText(find.byType(TextFormField).at(1), '- Discriminant decides the roots');
      await tester.tap(find.text('Publish note'));
      await tester.pumpAndSettle();
      expect(calls, ['POST /api/v1/sessions/sess-1/notes', 'POST /api/v1/notes/note-1/publish']);
      expect(body!['visibility'], 'Participants');
      expect(body!['title'], 'Key points (Fictional)');
    });

    testWidgets('an assignment is created for the class students and published', (tester) async {
      final calls = <String>[];
      Map<String, dynamic>? body;
      final services = await _signedIn((r) async {
        calls.add('${r.method} ${r.url.path}');
        if (r.url.path.endsWith('/assignments')) {
          body = jsonDecode(r.body) as Map<String, dynamic>;
          return http.Response(jsonEncode({'id': 'as-1'}), 200);
        }
        return http.Response('', 204);
      });
      await tester.pumpWidget(_host(AddAssignmentScreen(session: ended(over: true)), services));
      await tester.enterText(find.byType(TextFormField).at(0), 'Worksheet 5 (Fictional)');
      await tester.enterText(find.byType(TextFormField).at(1), 'Questions 1–10.');
      await tester.scrollUntilVisible(find.text('Publish assignment'), 200, scrollable: find.byType(Scrollable).first);
      await tester.tap(find.text('Publish assignment'));
      await tester.pumpAndSettle();
      expect(calls, ['POST /api/v1/assignments', 'POST /api/v1/assignments/as-1/publish']);
      expect(body!['sessionId'], 'sess-1');
      expect(body!['studentIds'], isNull, reason: 'the server targets the class participants');
      expect(body!['maxMarks'], 10);
      expect(DateTime.parse(body!['dueAtUtc'] as String).isAfter(DateTime.now().toUtc()), isTrue);
    });
  });

  testWidgets('My progress screen shows the attendance summary and every measure', (tester) async {
    final services = await _signedIn((r) async => r.url.path.endsWith('/dashboard/student')
        // UTF-8: the fixture contains an em dash.
        ? http.Response.bytes(utf8.encode(jsonEncode(studentDashboardJson)), 200, headers: {'content-type': 'application/json; charset=utf-8'})
        : http.Response('{}', 200));
    await tester.pumpWidget(AppScope(services: services, child: MaterialApp(theme: buildAppTheme(), home: const ProgressScreen())));
    // The loader spins until the (fake) API answers; pump until the content is there.
    for (var i = 0; i < 20 && find.text('All measures').evaluate().isEmpty; i++) {
      await tester.pump(const Duration(milliseconds: 100));
    }
    expect(find.text('My progress'), findsOneWidget);
    expect(find.text('Attendance'), findsWidgets, reason: 'ring label and the measure');
    expect(find.text('All measures'), findsOneWidget);
  });
}
