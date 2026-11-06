import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../features/auth/change_password_screen.dart';
import '../../features/auth/login_screen.dart';
import '../../features/auth/simple_screens.dart';
import '../../features/auth/welcome_screen.dart';
import '../../features/doubts/doubt_detail_screen.dart';
import '../../features/doubts/doubts_list_screen.dart';
import '../../features/doubts/new_doubt_screen.dart';
import '../../features/learning/assignment_detail_screen.dart';
import '../../features/learning/exam_attempt_screen.dart';
import '../../features/learning/exam_result_screen.dart';
import '../../features/learning/learning_screen.dart';
import '../../features/notifications/notifications_screen.dart';
import '../../features/parent/parent_screens.dart';
import '../../features/profile/profile_screen.dart';
import '../../features/profile/progress_screen.dart';
import '../../features/sessions/recording_player_screen.dart';
import '../../features/sessions/session_detail_screen.dart';
import '../../features/sessions/sessions_list_screen.dart';
import '../../features/sessions/timetable_screen.dart';
import '../../features/shell/role_shell.dart';
import '../../features/student/student_home_screen.dart';
import '../../features/subjects/subject_detail_screen.dart';
import '../../features/subjects/subjects_screen.dart';
import '../../features/teacher/students_screen.dart';
import '../../features/teacher/tasks_screen.dart';
import '../../features/teacher/teacher_dashboard_screen.dart';
import '../../features/videos/video_widgets.dart';
import '../auth/auth_controller.dart';
import '../auth/welcome_store.dart';
import '../models/content_models.dart';
import '../models/enums.dart';
import '../models/learning_models.dart';
import '../widgets/illustration.dart';
import '../widgets/states.dart';
import 'transitions.dart';

String homeFor(UserRole role) => switch (role) {
      UserRole.student => '/student/home',
      UserRole.teacher => '/teacher/home',
      UserRole.parent => '/parent/home',
      UserRole.institutionAdmin || UserRole.superAdmin => '/web-portal',
    };

const _publicPaths = {'/splash', '/welcome', '/login', '/change-password', '/web-portal'};

/// Pure redirect rule (unit-tested): where should [location] go for this auth state?
String? authRedirect({
  required AuthStatus status,
  required UserRole? role,
  required bool mustChangePassword,
  required String location,
  bool seenWelcome = true,
}) {
  if (status == AuthStatus.initializing) return location == '/splash' ? null : '/splash';
  // Signed-out visitors see the welcome screens once, then may only sign in (accounts are
  // created by the institute office).
  if (status == AuthStatus.signedOut || role == null) {
    if (!seenWelcome) return location == '/welcome' ? null : '/welcome';
    return location == '/login' || location == '/welcome' ? null : '/login';
  }
  if (mustChangePassword) return location == '/change-password' ? null : '/change-password';
  final home = homeFor(role);
  if (role == UserRole.institutionAdmin || role == UserRole.superAdmin) return location == '/web-portal' ? null : '/web-portal';
  if (_publicPaths.contains(location)) return home;
  // Keep each role inside its own navigation shell.
  for (final prefix in const ['/student/', '/teacher/', '/parent/']) {
    if (location.startsWith(prefix) && !home.startsWith(prefix)) return home;
  }
  return null;
}

GoRouter buildRouter(AuthController auth, WelcomeState welcome) {
  final rootKey = GlobalKey<NavigatorState>();
  return GoRouter(
    navigatorKey: rootKey,
    initialLocation: '/splash',
    refreshListenable: Listenable.merge([auth, welcome]),
    redirect: (context, state) => authRedirect(
      status: auth.status,
      role: auth.user?.role,
      mustChangePassword: auth.mustChangePassword,
      location: state.matchedLocation,
      seenWelcome: welcome.value,
    ),
    // Unknown deep links and stale notification links land on a friendly 404.
    errorBuilder: (context, state) => const NotFoundScreen(),
    routes: [
      GoRoute(path: '/splash', pageBuilder: (_, s) => fadePage(s, const SplashScreen())),
      GoRoute(path: '/welcome', pageBuilder: (_, s) => fadePage(s, const WelcomeScreen())),
      GoRoute(path: '/login', pageBuilder: (_, s) => fadePage(s, const LoginScreen())),
      GoRoute(path: '/change-password', pageBuilder: (_, s) => fadePage(s, const ChangePasswordScreen())),
      GoRoute(path: '/web-portal', pageBuilder: (_, s) => fadePage(s, const WebPortalScreen())),
      GoRoute(path: '/notifications', builder: (_, _) => const NotificationsScreen()),
      GoRoute(path: '/profile', builder: (_, _) => const ProfileScreen()),
      GoRoute(path: '/progress', builder: (_, _) => const ProgressScreen()),
      GoRoute(path: '/subjects/:id', builder: (_, s) => SubjectDetailScreen(subjectId: s.pathParameters['id']!)),
      // The full timetable (calendar, upcoming, past), opened from Subjects and Home.
      GoRoute(path: '/schedule', builder: (_, _) => const TimetableScreen()),
      GoRoute(
        path: '/sessions/:id',
        builder: (_, s) => SessionDetailScreen(sessionId: s.pathParameters['id']!, initialTab: s.uri.queryParameters['tab']),
      ),
      // The recording is passed as `extra`; without it (e.g. after a cold restart) send the user back to the class.
      GoRoute(
        path: '/recordings/:id/play',
        builder: (_, s) => s.extra is Recording
            ? RecordingPlayerScreen(media: PlayableMedia.recording(s.extra! as Recording))
            : const Scaffold(body: EmptyState(icon: LucideIcons.clapperboard, illustration: Illustration.notFound, title: 'Recording unavailable', message: 'Open this recording again from its class.')),
      ),
      GoRoute(path: '/videos', builder: (_, _) => const VideosScreen()),
      // Same pattern for pre-recorded videos. The server still refuses a link for a locked video.
      GoRoute(
        path: '/videos/:id/play',
        builder: (_, s) => s.extra is PrerecordedVideo && (s.extra! as PrerecordedVideo).isPlayable
            ? RecordingPlayerScreen(media: PlayableMedia.video(s.extra! as PrerecordedVideo))
            : const Scaffold(body: EmptyState(icon: LucideIcons.monitorPlay, illustration: Illustration.notFound, title: 'Video unavailable', message: 'Open this video again from its class.')),
      ),
      GoRoute(path: '/assignments/:id', builder: (_, s) => AssignmentDetailScreen(assignmentId: s.pathParameters['id']!)),
      GoRoute(path: '/exams/:id/attempt', builder: (_, s) => ExamAttemptScreen(examId: s.pathParameters['id']!)),
      GoRoute(
        path: '/attempts/:id/result',
        builder: (_, s) => ExamResultScreen(attemptId: s.pathParameters['id']!, initial: s.extra is AttemptResult ? s.extra! as AttemptResult : null),
      ),
      GoRoute(path: '/doubts/new', builder: (_, s) => NewDoubtScreen(sessionId: s.uri.queryParameters['sessionId'])),
      GoRoute(path: '/doubts/:id', builder: (_, s) => DoubtDetailScreen(doubtId: s.pathParameters['id']!)),
      GoRoute(
        path: '/grading/:assignmentId/:submissionId',
        builder: (_, s) => GradeSubmissionScreen(assignmentId: s.pathParameters['assignmentId']!, submissionId: s.pathParameters['submissionId']!),
      ),
      GoRoute(path: '/students/:id', builder: (_, s) => StudentProfileScreen(studentId: s.pathParameters['id']!)),

      // Each role has four tabs; Profile opens from the avatar in every tab's top bar.
      // Student: Home, Subjects (syllabus with each chapter's classes and videos), Learning, Doubts.
      _shell(const [
        ShellTab('/student/home', 'Home', LucideIcons.house, StudentHomeScreen()),
        ShellTab('/student/subjects', 'Subjects', LucideIcons.bookOpen, SubjectsScreen()),
        ShellTab('/student/learning', 'Learning', LucideIcons.graduationCap, LearningScreen()),
        ShellTab('/student/doubts', 'Doubts', LucideIcons.messageCircleQuestionMark, DoubtsListScreen(canAsk: true)),
      ]),
      // Teacher: Home, Classes, Students, Tasks (grading, attendance and doubts to answer).
      _shell(const [
        ShellTab('/teacher/home', 'Home', LucideIcons.layoutDashboard, TeacherDashboardScreen()),
        ShellTab('/teacher/classes', 'Classes', LucideIcons.calendarDays, SessionsListScreen(title: 'Classes', showTeacher: false, showVideosAction: true)),
        ShellTab('/teacher/students', 'Students', LucideIcons.usersRound, StudentsScreen()),
        ShellTab('/teacher/tasks', 'Tasks', LucideIcons.listTodo, TasksScreen()),
      ]),
      // Parent (retained, cannot sign in): Home, Children, Calendar, Progress.
      _shell(const [
        ShellTab('/parent/home', 'Home', LucideIcons.house, ParentHomeScreen()),
        ShellTab('/parent/children', 'Children', LucideIcons.usersRound, ParentChildrenScreen()),
        ShellTab('/parent/calendar', 'Calendar', LucideIcons.calendarDays, ParentCalendarScreen()),
        ShellTab('/parent/progress', 'Progress', LucideIcons.chartLine, ParentProgressScreen()),
      ]),
    ],
  );
}

StatefulShellRoute _shell(List<ShellTab> tabs) => StatefulShellRoute(
      // Arriving from sign-in fades in; switching tabs fades the new tab in.
      pageBuilder: (context, state, shell) => fadePage(state, RoleShell(shell: shell, tabs: tabs)),
      navigatorContainerBuilder: (context, shell, children) => FadeIndexedStack(index: shell.currentIndex, children: children),
      branches: [
        for (final tab in tabs)
          StatefulShellBranch(routes: [GoRoute(path: tab.path, builder: (_, _) => tab.screen)]),
      ],
    );
