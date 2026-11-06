import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:student_sync/core/api/api_exception.dart';
import 'package:student_sync/core/app_services.dart';
import 'package:student_sync/core/auth/token_store.dart';
import 'package:student_sync/core/config/app_config.dart';
import 'package:student_sync/core/models/common.dart';
import 'package:student_sync/core/models/enums.dart';
import 'package:student_sync/core/models/json.dart';
import 'package:student_sync/core/models/learning_models.dart';
import 'package:student_sync/core/theme/app_theme.dart';
import 'package:student_sync/core/theme/status_styles.dart';
import 'package:student_sync/core/theme/tokens.dart';
import 'package:student_sync/core/widgets/common.dart';
import 'package:student_sync/core/widgets/avatar.dart';
import 'package:student_sync/core/widgets/states.dart';
import 'package:student_sync/features/auth/change_password_screen.dart';
import 'package:student_sync/features/auth/login_screen.dart';
import 'package:student_sync/features/doubts/doubts_list_screen.dart';
import 'package:student_sync/features/learning/question_input.dart';

import '../support/fixtures.dart';

Widget _host(Widget child, {AppServices? services}) {
  final app = MaterialApp(theme: buildAppTheme(), home: child);
  return services == null ? app : AppScope(services: services, child: app);
}

AppServices _services(http.Client client) => AppServices.create(
      config: const AppConfig(apiBaseUrl: 'https://api.example.test/api/v1', clientName: 'test'),
      httpClient: client,
      tokenStore: InMemoryTokenStore(),
    );

void main() {
  group('Login screen', () {
    testWidgets('validates email and password before calling the API', (tester) async {
      var calls = 0;
      final services = _services(MockClient((_) async {
        calls++;
        return http.Response('{}', 500);
      }));
      await services.auth.restore();
      await tester.pumpWidget(_host(const LoginScreen(), services: services));

      await tester.tap(find.byKey(const Key('login.submit')));
      await tester.pump();
      expect(find.text('Enter your email address.'), findsOneWidget);
      expect(find.text('Enter your password.'), findsOneWidget);

      await tester.enterText(find.byKey(const Key('login.email')), 'not-an-email');
      await tester.tap(find.byKey(const Key('login.submit')));
      await tester.pump();
      expect(find.textContaining('Enter a valid email address'), findsOneWidget);
      expect(calls, 0, reason: 'no request is sent for invalid input');
    });

    testWidgets('shows the institution code field when the server requires it', (tester) async {
      final services = _services(MockClient((req) async => http.Response(
            jsonEncode({'title': 'This email is used at more than one institution. Enter your institution code.', 'status': 400, 'code': 'institution_code_required'}),
            400,
          )));
      await services.auth.restore();
      await tester.pumpWidget(_host(const LoginScreen(), services: services));

      expect(find.byKey(const Key('login.institution')), findsNothing);
      await tester.enterText(find.byKey(const Key('login.email')), 'shared@example.test');
      await tester.enterText(find.byKey(const Key('login.password')), 'whatever');
      await tester.tap(find.byKey(const Key('login.submit')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('login.institution')), findsOneWidget);
      expect(find.textContaining('more than one institution'), findsWidgets);
    });

    testWidgets('shows the server message for wrong credentials', (tester) async {
      final services = _services(MockClient((req) async => http.Response(
            jsonEncode({'title': 'The email or password is incorrect.', 'status': 401, 'code': 'invalid_credentials'}),
            401,
          )));
      await services.auth.restore();
      await tester.pumpWidget(_host(const LoginScreen(), services: services));
      await tester.enterText(find.byKey(const Key('login.email')), 'a@example.test');
      await tester.enterText(find.byKey(const Key('login.password')), 'bad');
      await tester.tap(find.byKey(const Key('login.submit')));
      await tester.pumpAndSettle();
      expect(find.text('The email or password is incorrect.'), findsOneWidget);
    });
  });

  test('new password rules mirror the server policy', () {
    expect(validateNewPassword('short1A'), isNotNull);
    expect(validateNewPassword('alllowercase1'), contains('upper-case'));
    expect(validateNewPassword('ALLUPPERCASE1'), contains('lower-case'));
    expect(validateNewPassword('NoDigitsHere'), contains('number'));
    expect(validateNewPassword('GoodPassword1'), isNull);
  });

  group('Status styles', () {
    test('every status maps to the DESIGN.md label and colour', () {
      expect(StatusStyles.session(SessionStatus.scheduled), const StatusStyle('Scheduled', SsColors.blue));
      expect(StatusStyles.session(SessionStatus.live).color, SsColors.orange);
      expect(StatusStyles.session(SessionStatus.completed).color, SsColors.success);
      expect(StatusStyles.session(SessionStatus.cancelled).strikeThrough, isTrue);
      expect(StatusStyles.attendance(AttendanceStatus.present).color, SsColors.success);
      expect(StatusStyles.attendance(AttendanceStatus.late).color, SsColors.warning);
      expect(StatusStyles.attendance(AttendanceStatus.absent).color, SsColors.error);
      expect(StatusStyles.attendance(AttendanceStatus.excused).color, SsColors.slate);
      expect(StatusStyles.attendance(null).label, 'Not marked');
      expect(StatusStyles.doubt(DoubtStatus.newDoubt).color, SsColors.blue);
      expect(StatusStyles.doubt(DoubtStatus.inProgress).color, SsColors.info);
      expect(StatusStyles.doubt(DoubtStatus.answered).color, SsColors.warning);
      expect(StatusStyles.doubt(DoubtStatus.resolved).color, SsColors.success);
      expect(StatusStyles.doubt(DoubtStatus.reopened).color, SsColors.error);
      // Exhaustive: every enum value has a non-empty label.
      for (final s in DoubtStatus.values) {
        expect(StatusStyles.doubt(s).label, isNotEmpty);
      }
      for (final s in SubmissionStatus.values) {
        expect(StatusStyles.submission(s).label, isNotEmpty);
      }
    });

    test('teaching modes use the product labels', () {
      expect(SessionType.values.map(StatusStyles.sessionTypeLabel), ['Batch class', 'Group session', 'One-to-one', 'Private group']);
    });

    testWidgets('StatusBadge renders its label (not colour alone) with a semantic label', (tester) async {
      final handle = tester.ensureSemantics();
      await tester.pumpWidget(_host(Scaffold(body: Center(child: StatusBadge(StatusStyles.attendance(AttendanceStatus.absent))))));
      expect(find.text('Absent'), findsOneWidget);
      expect(find.bySemanticsLabel('Status: Absent'), findsOneWidget);
      handle.dispose();
    });
  });

  group('States', () {
    testWidgets('ErrorState distinguishes offline and forbidden, and offers Retry', (tester) async {
      var retried = 0;
      await tester.pumpWidget(_host(Scaffold(body: ErrorState(error: ApiException.network(), onRetry: () => retried++))));
      expect(find.text("You're offline"), findsOneWidget);
      await tester.tap(find.text('Retry'));
      expect(retried, 1);

      await tester.pumpWidget(_host(const Scaffold(
        body: ErrorState(error: ApiException(statusCode: 403, code: 'forbidden', message: 'You do not have permission to perform this action.')),
      )));
      expect(find.text("You don't have access to this"), findsOneWidget);
      expect(find.text('Retry'), findsNothing);
    });
  });

  group('Exam question rendering', () {
    final attempt = Attempt.fromJson(jsonDecode(jsonEncode(attemptJson)) as Json);
    AttemptQuestion q(QuestionType type) => attempt.questions.firstWhere((x) => x.type == type);

    Future<List<ExamResponse>> pumpQuestion(WidgetTester tester, AttemptQuestion question) async {
      final changes = <ExamResponse>[];
      await tester.pumpWidget(_host(Scaffold(
        body: SingleChildScrollView(child: QuestionInput(question: question, response: question.savedResponse, onChanged: changes.add, index: 0, total: 7)),
      )));
      return changes;
    }

    testWidgets('single choice sends the chosen option id', (tester) async {
      final question = q(QuestionType.singleChoice);
      final changes = await pumpQuestion(tester, question);
      expect(find.text(question.stem), findsOneWidget);
      expect(find.text('Choose one answer'), findsOneWidget);
      await tester.tap(find.text('x = 2'));
      await tester.pump();
      expect(changes.single.optionIds, ['o1000000-0000-0000-0000-000000000001']);
    });

    testWidgets('true/false renders both options as a single choice', (tester) async {
      final changes = await pumpQuestion(tester, q(QuestionType.trueFalse));
      expect(find.text('True'), findsOneWidget);
      expect(find.text('False'), findsOneWidget);
      await tester.tap(find.text('False'));
      expect(changes.single.optionIds, ['o3000000-0000-0000-0000-000000000002']);
    });

    testWidgets('multiple choice keeps the saved selection and adds to it in option order', (tester) async {
      final changes = await pumpQuestion(tester, q(QuestionType.multipleChoice));
      expect(tester.widget<CheckboxListTile>(find.widgetWithText(CheckboxListTile, '2')).value, isTrue);
      await tester.tap(find.text('4'));
      expect(changes.single.optionIds, ['o2000000-0000-0000-0000-000000000001', 'o2000000-0000-0000-0000-000000000003']);
    });

    testWidgets('numerical validates input and sends a number', (tester) async {
      final changes = await pumpQuestion(tester, q(QuestionType.numerical));
      expect(find.text('10'), findsOneWidget, reason: 'saved response is restored');
      await tester.enterText(find.byKey(const Key('question.number')), 'abc');
      await tester.pump();
      expect(find.textContaining('Enter a number'), findsOneWidget);
      expect(changes, isEmpty);
      await tester.enterText(find.byKey(const Key('question.number')), '12,5');
      await tester.pump();
      expect(changes.single.number, 12.5);
    });

    testWidgets('fill-in-the-blank and long answer send text', (tester) async {
      var changes = await pumpQuestion(tester, q(QuestionType.fillBlank));
      expect(find.text('Word or phrase for the blank'), findsOneWidget);
      await tester.enterText(find.byKey(const Key('question.text')), 'quadratic');
      expect(changes.last.text, 'quadratic');

      changes = await pumpQuestion(tester, q(QuestionType.longAnswer));
      expect(find.text('Long answer'), findsOneWidget);
      await tester.enterText(find.byKey(const Key('question.text')), 'Add and subtract (b/2)².');
      expect(changes.last.text, 'Add and subtract (b/2)².');
    });

    testWidgets('matching shows a dropdown per item and keeps existing matches', (tester) async {
      final question = q(QuestionType.matching);
      final changes = await pumpQuestion(tester, question);
      expect(find.text('Discriminant'), findsOneWidget);
      expect(find.text('Turning point'), findsOneWidget, reason: 'saved match restored');
      await tester.tap(find.byKey(const Key('match.o6000000-0000-0000-0000-000000000001')));
      await tester.pumpAndSettle();
      await tester.tap(find.text('b² − 4ac').last);
      await tester.pumpAndSettle();
      expect(changes.single.matches, {
        'o6000000-0000-0000-0000-000000000002': 'Turning point',
        'o6000000-0000-0000-0000-000000000001': 'b² − 4ac',
      });
    });
  });

  testWidgets('MetricTile reveals how a metric is calculated', (tester) async {
    await tester.pumpWidget(_host(Scaffold(
      body: MetricTile(Metric.fromJson(jsonDecode(jsonEncode(metric('Attendance', '100%', percent: 100))) as Json)),
    )));
    expect(find.text('Attendance'), findsOneWidget);
    expect(find.text('100%'), findsOneWidget);
    expect(find.text('How Attendance is calculated.'), findsNothing);
    await tester.tap(find.text('Attendance'));
    await tester.pumpAndSettle();
    expect(find.text('How Attendance is calculated.'), findsOneWidget);
  });

  test('avatar initials ignore bracketed suffixes', () {
    expect(UserAvatar.initials('Aisha Khan (Demo Student)'), 'AK');
    expect(UserAvatar.initials('Amira Haddad (Demo Teacher)'), 'AH');
    expect(UserAvatar.initials('Priya'), 'P');
    expect(UserAvatar.initials('  '), '?');
  });

  test('doubts waiting on a teacher or student are open; answered and resolved are not', () {
    expect([for (final s in DoubtStatus.values) if (isOpenDoubt(s)) s],
        [DoubtStatus.newDoubt, DoubtStatus.assigned, DoubtStatus.inProgress, DoubtStatus.reopened]);
  });

  testWidgets('pill segmented control reports the tapped option and marks the selected one', (tester) async {
    final handle = tester.ensureSemantics();
    var selected = 'open';
    await tester.pumpWidget(_host(Scaffold(
      body: StatefulBuilder(
        builder: (context, setState) => SsSegmented<String>(
          segments: const [(value: 'open', label: 'Open', icon: null), (value: 'all', label: 'All', icon: null)],
          selected: selected,
          onChanged: (v) => setState(() => selected = v),
        ),
      ),
    )));
    await tester.tap(find.text('All'));
    await tester.pumpAndSettle();
    expect(selected, 'all');
    expect(tester.getSemantics(find.text('All')), isSemantics(isButton: true, isSelected: true, label: 'All'));
    handle.dispose();
  });
}
