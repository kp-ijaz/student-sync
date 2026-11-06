// On-device, read-only walkthrough against a running local API using the fictional
// demo accounts. It never creates, submits, grades or marks anything.
//
// The pre-recorded video shots expect two fictional "(Test)" videos on Amira's
// G10 Maths classes (one past and unlocked, one upcoming and still locked); upload
// them from the web app's class page first.
//
// The demo password is NOT stored in the repo. Pass it at run time:
//   flutter test integration_test/demo_walkthrough_test.dart -d <simulator-id> \
//     --dart-define=DEMO_PASSWORD=<value from the repo's .env>
//
// Each `SHOT:<name>` line printed to the console marks a moment where the screen is
// stable for ~2.5 s; scripts/capture_screenshots.sh uses these to take simulator
// screenshots with `xcrun simctl io booted screenshot`.
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';
import 'package:student_sync/app.dart';
import 'package:student_sync/core/app_services.dart';
import 'package:student_sync/core/auth/welcome_store.dart';
import 'package:student_sync/core/config/app_config.dart';
import 'package:student_sync/core/models/json.dart';
import 'package:student_sync/core/models/learning_models.dart';
import 'package:student_sync/core/theme/app_theme.dart';
import 'package:student_sync/core/widgets/common.dart';
import 'package:student_sync/core/widgets/session_card.dart';
import 'package:student_sync/features/learning/exam_attempt_screen.dart';
import 'package:timezone/data/latest_all.dart' as tzdata;

import '../test/support/fixtures.dart';

const _password = String.fromEnvironment('DEMO_PASSWORD');

Future<void> _settleUntil(WidgetTester tester, Finder finder, {Duration timeout = const Duration(seconds: 20)}) async {
  final end = DateTime.now().add(timeout);
  while (DateTime.now().isBefore(end)) {
    await tester.pump(const Duration(milliseconds: 100));
    if (finder.evaluate().isNotEmpty) {
      // Let images/fonts/animations finish.
      for (var i = 0; i < 5; i++) {
        await tester.pump(const Duration(milliseconds: 100));
      }
      return;
    }
  }
  debugPrint('SHOT:zz_failure');
  await Future<void>.delayed(const Duration(seconds: 3));
  throw TestFailure('Timed out waiting for $finder');
}

Future<void> _shot(WidgetTester tester, String name) async {
  // Give the device a moment to present the latest frames before capturing.
  for (var i = 0; i < 10; i++) {
    await tester.pump(const Duration(milliseconds: 100));
    await Future<void>.delayed(const Duration(milliseconds: 100));
  }
  debugPrint('SHOT:$name');
  final end = DateTime.now().add(const Duration(milliseconds: 2500));
  while (DateTime.now().isBefore(end)) {
    await tester.pump(const Duration(milliseconds: 100));
  }
}

Future<void> _signIn(WidgetTester tester, String email) async {
  await _settleUntil(tester, find.byKey(const Key('login.email')));
  await tester.enterText(find.byKey(const Key('login.email')), email);
  await tester.enterText(find.byKey(const Key('login.password')), _password);
  await tester.testTextInput.receiveAction(TextInputAction.done);
  await tester.pump();
}

Future<void> _tapNav(WidgetTester tester, String label) async {
  await tester.tap(find.byTooltip(label).last);
  await tester.pump(const Duration(milliseconds: 300));
}

/// The page's own scroll view: the tallest vertical scrollable on screen (small ones
/// inside text fields, chips or carousels are ignored).
Finder _pageScrollable() {
  final candidates = find
      .byWidgetPredicate((w) => w is Scrollable && axisDirectionToAxis(w.axisDirection) == Axis.vertical)
      .evaluate()
      .where((e) => e.renderObject is RenderBox && (e.renderObject! as RenderBox).hasSize)
      .toList()
    ..sort((a, b) => (b.renderObject! as RenderBox).size.height.compareTo((a.renderObject! as RenderBox).size.height));
  final page = candidates.first;
  return find.byElementPredicate((e) => e == page);
}

/// Sections live on one scrolling page; bring [finder] into view.
Future<void> _scrollTo(WidgetTester tester, Finder finder, {double delta = 300}) async {
  await _settleUntil(tester, find.byType(Scrollable));
  await tester.scrollUntilVisible(finder, delta, scrollable: _pageScrollable(), maxScrolls: 80);
  // Content scrolls behind the floating navigation bar; place the target near the top.
  await Scrollable.ensureVisible(tester.element(finder.first), alignment: 0.3);
  await tester.pump(const Duration(milliseconds: 300));
}

/// Taps a tab by its label (labels can also appear elsewhere on the page).
Future<void> _tapTab(WidgetTester tester, String label) =>
    tester.tap(find.descendant(of: find.byType(TabBar), matching: find.text(label)).first);

Future<void> _back(WidgetTester tester) async {
  // The video player shows its own Back button over the app bar's; use the top-most.
  final backs = find.byTooltip('Back');
  if (backs.evaluate().length > 1) {
    await tester.tap(backs.last);
  } else {
    await tester.pageBack();
  }
  // Let the page transition finish before the next step.
  for (var k = 0; k < 10; k++) {
    await tester.pump(const Duration(milliseconds: 100));
  }
}

Future<void> _signOut(WidgetTester tester) async {
  // Profile opens from the avatar in each tab's top bar (it is not a tab).
  final onProfile = find.widgetWithText(AppBar, 'Profile');
  if (onProfile.evaluate().isEmpty) await _tapNav(tester, 'Profile');
  // Sign out is at the bottom of Profile (below the student's progress).
  await _settleUntil(tester, onProfile);
  await _scrollTo(tester, find.widgetWithText(OutlinedButton, 'Sign out'));
  await tester.tap(find.widgetWithText(OutlinedButton, 'Sign out'));
  await _settleUntil(tester, find.widgetWithText(FilledButton, 'Sign out'));
  await tester.tap(find.widgetWithText(FilledButton, 'Sign out'));
  await _settleUntil(tester, find.byKey(const Key('login.email')));
}

void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  testWidgets('demo walkthrough: student, teacher, refused parent, admin (read-only)', (tester) async {
    if (_password.isEmpty) {
      markTestSkipped('DEMO_PASSWORD not provided');
      return;
    }
    tzdata.initializeTimeZones();
    // Start as a first-time user so the welcome screens are captured too.
    final services = AppServices.create(config: AppConfig.fromEnvironment(), welcomeStore: InMemoryWelcomeStore(initiallySeen: false));
    await services.auth.restore();
    await services.welcome.load();
    if (services.auth.isSignedIn) await services.signOut();
    await tester.pumpWidget(StudentSyncApp(services: services));

    // ---- Welcome ----
    await _settleUntil(tester, find.byKey(const Key('welcome.next')));
    await _shot(tester, '00a_welcome_1');
    await tester.tap(find.byKey(const Key('welcome.next')));
    await tester.pump(const Duration(milliseconds: 500));
    await _shot(tester, '00b_welcome_2');
    await tester.tap(find.byKey(const Key('welcome.next')));
    await tester.pump(const Duration(milliseconds: 500));
    await _shot(tester, '00c_welcome_3');
    await tester.tap(find.byKey(const Key('welcome.next')));

    // ---- Login ----
    await _settleUntil(tester, find.byKey(const Key('login.email')));
    await _shot(tester, '01_login');
    await tester.tap(find.byKey(const Key('login.register')));
    await _settleUntil(tester, find.byKey(const Key('register.submit')));
    await _shot(tester, '01b_register_form');
    await tester.tap(find.byTooltip('Back to sign in'));
    await _settleUntil(tester, find.byKey(const Key('login.email')));

    // ---- Student (Aisha: batch classes, notes, exam) ----
    await _signIn(tester, 'aisha@horizon.example.test');
    await _settleUntil(tester, find.byType(NextClassHero));
    await _shot(tester, '02_student_home');
    await tester.drag(find.byType(Scrollable).first, const Offset(0, -700));
    await _shot(tester, '03_student_home_scrolled');

    // Subjects (grouped core / languages / additional) → one subject's tabs.
    await _tapNav(tester, 'Subjects');
    await _settleUntil(tester, find.text('Core subjects'));
    await _shot(tester, '03a_student_subjects');
    await tester.tap(find.text('Mathematics').first);
    await _settleUntil(tester, find.text('Syllabus'));
    await _shot(tester, '03c_student_subject_videos');
    if (find.widgetWithText(FilledButton, 'Watch').evaluate().isNotEmpty) {
      await tester.tap(find.widgetWithText(FilledButton, 'Watch').first);
      await _settleUntil(tester, find.byTooltip('Pause'));
      await _shot(tester, '05c_student_video_player');
      await _back(tester);
    }
    await _tapTab(tester, 'Classes');
    await _settleUntil(tester, find.textContaining('Past classes'));
    await tester.tap(find.textContaining('Past classes'));
    await tester.pump(const Duration(milliseconds: 500));
    await _shot(tester, '03d_student_subject_classes');
    // A past class: its notes and the student's own attendance.
    await tester.tap(find.text('Completed').first);
    await _settleUntil(tester, find.text('Teacher'));
    await _shot(tester, '05_student_session_overview');
    await _scrollTo(tester, find.text('Your attendance'));
    await _shot(tester, '07_student_own_attendance');
    await _back(tester);
    await _tapTab(tester, 'Syllabus');
    await tester.pump(const Duration(seconds: 1));
    await _shot(tester, '03f_student_subject_syllabus');
    await _back(tester);
    // Timetable: the day hour by hour, and the month.
    await tester.tap(find.byTooltip('Timetable'));
    await _settleUntil(tester, find.byTooltip('Next week'));
    await _shot(tester, '03b_student_timetable_day');
    await tester.tap(find.text('Month'));
    await _settleUntil(tester, find.byTooltip('Next month'));
    await _shot(tester, '04_student_timetable_month');
    await _back(tester);
    await _tapNav(tester, 'Learning');
    await _settleUntil(tester, find.textContaining('Worksheet'));
    await _shot(tester, '08_student_assignments');
    await _tapTab(tester, 'Exams');
    await _settleUntil(tester, find.text('Quadratics checkpoint quiz'));
    await _shot(tester, '09_student_exams');
    await _tapTab(tester, 'Videos');
    await _settleUntil(tester, find.text('Completing the square — preview (Test)'));
    await _shot(tester, '09b_student_videos_locked');
    await _tapTab(tester, 'Assignments');
    await _settleUntil(tester, find.textContaining('Worksheet'));
    await tester.tap(find.textContaining('Worksheet').first);
    await _settleUntil(tester, find.text('Instructions'));
    await _shot(tester, '10_student_assignment_detail');
    await _back(tester);

    await _tapNav(tester, 'Doubts');
    await _settleUntil(tester, find.text('Ask a doubt'));
    await _settleUntil(tester, find.text('Answered & closed'));
    await _shot(tester, '11_student_doubts');
    await tester.tap(find.text('Ask a doubt'));
    await _settleUntil(tester, find.text('Send to teacher'));
    await _shot(tester, '12_student_new_doubt_form');
    await _back(tester);

    await _tapNav(tester, 'Profile');
    await _settleUntil(tester, find.text('My progress'));
    await _shot(tester, '13_student_profile');
    await tester.tap(find.text('My progress'));
    await _settleUntil(tester, find.text('All measures'));
    await _shot(tester, '13c_student_progress');
    await _back(tester);
    await _scrollTo(tester, find.widgetWithText(SsListRow, 'Notifications'));
    await _shot(tester, '13b_student_profile_account');
    await tester.tap(find.widgetWithText(SsListRow, 'Notifications'));
    await _settleUntil(tester, find.text('Mark all read'));
    await tester.pump(const Duration(seconds: 2));
    await _shot(tester, '14_notifications');
    await _back(tester);
    await _signOut(tester);

    // ---- Student (Fatima: one-to-one, no batch, open doubt) ----
    await _signIn(tester, 'fatima@horizon.example.test');
    await _settleUntil(tester, find.byType(NextClassHero));
    await _shot(tester, '15_student_one_to_one_home');
    await _tapNav(tester, 'Doubts');
    await _settleUntil(tester, find.text('Ask a doubt'));
    // Earlier E2E runs may have added doubts above the seeded one.
    await _scrollTo(tester, find.text('How many electrons fit in the third shell?'));
    await tester.tap(find.text('How many electrons fit in the third shell?'));
    await _settleUntil(tester, find.text('Asked by'));
    await _shot(tester, '16_student_doubt_detail');
    await _back(tester);
    await _signOut(tester);

    // ---- Teacher (Amira: batch) ----
    await _signIn(tester, 'amira@horizon.example.test');
    await _settleUntil(tester, find.byType(NextClassHero));
    await _shot(tester, '17_teacher_dashboard');
    await tester.drag(find.byType(Scrollable).first, const Offset(0, -700));
    await _shot(tester, '18_teacher_dashboard_scrolled');
    await _tapNav(tester, 'Classes');
    await _settleUntil(tester, find.text('Upcoming'));
    await tester.tap(find.byTooltip('Pre-recorded videos'));
    await _settleUntil(tester, find.text('Completing the square — preview (Test)'));
    await _shot(tester, '18b_teacher_videos');
    await _back(tester);
    await tester.tap(find.text('Past'));
    await _settleUntil(tester, find.text('G10 Maths — Evening A'));
    await tester.tap(find.text('Completed').first);
    await _settleUntil(tester, find.text('Teacher'));
    await _scrollTo(tester, find.text('Class preparation'));
    await _scrollTo(tester, find.text('Agenda'));
    await _shot(tester, '19_teacher_preparation');
    await _scrollTo(tester, find.text('No changes to save'));
    await _shot(tester, '20_teacher_attendance_editor');
    await _back(tester);
    await _tapNav(tester, 'Students');
    await _settleUntil(tester, find.textContaining('Aisha Khan'));
    await _shot(tester, '21_teacher_students');
    await tester.tap(find.textContaining('Aisha Khan').first);
    await _settleUntil(tester, find.text('Time zone'));
    await _shot(tester, '22_teacher_student_profile');
    await _back(tester);
    await _tapNav(tester, 'Tasks');
    await _settleUntil(tester, find.textContaining('Worksheet 4'));
    await _shot(tester, '23_teacher_tasks');
    await tester.tap(find.textContaining('Worksheet 4').first);
    await _settleUntil(tester, find.text('Answer'));
    await _shot(tester, '24_teacher_grade_form');
    await _back(tester);
    // Teachers answer doubts further down the Tasks page.
    await _settleUntil(tester, find.text('Doubts to answer'));
    await _scrollTo(tester, find.text('Why do we split the middle term?'));
    await tester.tap(find.text('Why do we split the middle term?'));
    await _settleUntil(tester, find.text('Mark as answered'));
    await _shot(tester, '25_teacher_doubt_reply');
    await _back(tester);
    await _signOut(tester);

    // ---- Parents have no portal at this institute: the server refuses sign-in ----
    await _signIn(tester, 'parent.almansoori@horizon.example.test');
    await _settleUntil(tester, find.textContaining('Parent accounts are not used'));
    await _shot(tester, '26_parent_refused');

    // ---- Admin gets the web-portal screen ----
    await _signIn(tester, 'admin@horizon.example.test');
    await _settleUntil(tester, find.text('Please use the web portal'));
    await _shot(tester, '31_admin_web_portal');
    await tester.tap(find.text('Sign out'));
    await _settleUntil(tester, find.byKey(const Key('login.email')));
  });

  // Exam-taking UI rendered with FIXTURE data (no API call), so the walkthrough never
  // consumes a demo student's single exam attempt.
  testWidgets('exam attempt UI (fixture data, offline)', (tester) async {
    tzdata.initializeTimeZones();
    final services = AppServices.create(config: AppConfig.fromEnvironment());
    final json = jsonDecode(jsonEncode(attemptJson)) as Json;
    final start = DateTime.now().toUtc();
    json['startedAtUtc'] = toUtcIso(start);
    json['deadlineUtc'] = toUtcIso(start.add(const Duration(minutes: 29, seconds: 41)));
    final attempt = Attempt.fromJson(json);
    await tester.pumpWidget(AppScope(
      services: services,
      child: MaterialApp(debugShowCheckedModeBanner: false, theme: buildAppTheme(), home: AttemptView(attempt: attempt, api: services.learning)),
    ));
    await _settleUntil(tester, find.text(attempt.questions.first.stem));
    await _shot(tester, '32_exam_attempt_fixture_q1');
    await tester.tap(find.text('6'));
    await _settleUntil(tester, find.text('Match each term'));
    await _shot(tester, '33_exam_attempt_fixture_matching');
  });
}
