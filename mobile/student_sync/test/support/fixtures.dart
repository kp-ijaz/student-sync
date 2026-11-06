// Sample payloads shaped exactly like the C# contracts in src/StudentSync.Contracts
// (camelCase, PascalCase enum strings, UTC ISO-8601 with Z). All data is fictional.

const userJson = {
  'id': '0bb54952-e8c7-478d-54bb-08df37830663',
  'fullName': 'Test Student (Fictional)',
  'email': 'student@example.test',
  'role': 'Student',
  'tenantId': 'c20e22ef-c922-46a5-0d19-08df3783066e',
  'tenantName': 'Example Academy (Fictional)',
  'timeZoneId': 'Asia/Dubai',
  'preferredLanguage': 'en',
  'mustChangePassword': false,
  'permissions': ['sessions.view', 'doubts.ask'],
  'enabledFeatures': ['exams'],
};

Map<String, Object?> authJson({
  required String access,
  required String refresh,
  required DateTime accessExpires,
  DateTime? refreshExpires,
  Map<String, Object?> user = userJson,
}) =>
    {
      'accessToken': access,
      'accessTokenExpiresAtUtc': accessExpires.toUtc().toIso8601String(),
      'refreshToken': refresh,
      'refreshTokenExpiresAtUtc': (refreshExpires ?? accessExpires.add(const Duration(days: 30))).toUtc().toIso8601String(),
      'user': user,
    };

const sessionListItemJson = {
  'id': '11ff8ff9-32fd-445b-fd2c-08df37830714',
  'title': 'Chemistry 1:1',
  'sessionType': 'OneToOne',
  'status': 'Scheduled',
  'startUtc': '2026-11-07T06:00:00Z',
  'endUtc': '2026-11-07T07:00:00Z',
  'timeZoneId': 'Asia/Dubai',
  'teacherId': '27936479-cca8-4969-54b4-08df37830663',
  'teacherName': 'Test Teacher',
  'batchId': null,
  'batchName': null,
  'subjectName': 'Chemistry',
  'participantCount': 1,
  'deliveryMode': 'Online',
  'hasMeetingLink': true,
  'isRecurring': true,
};

Map<String, Object?> metric(String label, String value, {double? percent}) =>
    {'label': label, 'value': value, 'howCalculated': 'How $label is calculated.', 'percent': percent};

final progressJson = {
  'studentId': '0bb54952-e8c7-478d-54bb-08df37830663',
  'attendance': metric('Attendance', '100%', percent: 100),
  'assignmentsSubmitted': metric('Assignments submitted', '1/1', percent: 100),
  'averageAssignmentMark': metric('Average assignment mark', '80%', percent: 80),
  'averageExamScore': metric('Average exam score', '—'),
  'openDoubts': metric('Open doubts', '1'),
  'lessonsAttended': metric('Lessons attended', '3'),
  'activeWeeks': metric('Active weeks', '2'),
};

final studentDashboardJson = {
  'firstName': 'Test',
  'nextSession': sessionListItemJson,
  'upcomingSessions': [sessionListItemJson],
  'dueAssignments': [
    {
      'id': '7b70ae73-59e9-46d4-73de-08df37830738',
      'title': 'Atomic structure summary',
      'status': 'Published',
      'dueAtUtc': '2026-10-28T19:13:19.8648817Z',
      'maxMarks': 10.0,
      'teacherName': 'Test Teacher',
      'sessionTitle': 'Chemistry 1:1',
      'recipientCount': 1,
      'submittedCount': 1,
      'gradedCount': 1,
      'mySubmissionStatus': 'Graded',
      'myMarks': 8.5,
    }
  ],
  'openDoubts': [
    {
      'id': '8661d6f1-e1dd-46f5-7488-08df3783074c',
      'title': 'How many electrons fit in the third shell?',
      'status': 'New',
      'priority': 'Normal',
      'studentName': 'Test Student',
      'subjectName': 'Chemistry',
      'assignedTeacherName': null,
      'sessionId': null,
      'followUpSessionId': null,
      'followUpSessionStartUtc': null,
      'createdAtUtc': '2026-10-31T19:13:21.282764Z',
      'lastActivityAtUtc': '2026-10-31T16:13:19.864881Z',
      'replyCount': 0,
    }
  ],
  'recentContent': [
    {'kind': 'Note', 'id': '5ae4a097-81ad-4370-0d5b-08df37830735', 'sessionId': '5e168ca6-de99-4d93-fd22-08df37830714', 'title': 'Factorisation', 'sessionTitle': 'G10 Maths', 'atUtc': '2026-10-28T16:00:00Z'}
  ],
  'progress': progressJson,
  'notifications': [
    {'id': '14facd55-6fff-4a8d-1ba4-08df3783076f', 'type': 'DoubtFollowUp', 'title': 'Your doubt', 'body': 'Body', 'link': '/doubts/8661d6f1-e1dd-46f5-7488-08df3783074c', 'createdAtUtc': '2026-10-31T18:13:19.864881Z', 'isRead': false}
  ],
};

final attemptJson = {
  'attemptId': 'a1111111-1111-1111-1111-111111111111',
  'examId': 'e1111111-1111-1111-1111-111111111111',
  'examTitle': 'Quadratics quiz',
  'status': 'InProgress',
  'startedAtUtc': '2026-11-01T10:00:00Z',
  'deadlineUtc': '2026-11-01T10:30:00Z',
  'attemptNumber': 1,
  'questions': [
    {
      'examQuestionId': 'q1000000-0000-0000-0000-000000000001',
      'type': 'SingleChoice',
      'stem': 'Which is a root of x² − 5x + 6 = 0?',
      'marks': 2.0,
      'options': [
        {'id': 'o1000000-0000-0000-0000-000000000001', 'text': 'x = 2'},
        {'id': 'o1000000-0000-0000-0000-000000000002', 'text': 'x = 5'},
      ],
      'matchChoices': null,
      'savedResponse': null,
    },
    {
      'examQuestionId': 'q1000000-0000-0000-0000-000000000002',
      'type': 'MultipleChoice',
      'stem': 'Select all even numbers',
      'marks': 2.0,
      'options': [
        {'id': 'o2000000-0000-0000-0000-000000000001', 'text': '2'},
        {'id': 'o2000000-0000-0000-0000-000000000002', 'text': '3'},
        {'id': 'o2000000-0000-0000-0000-000000000003', 'text': '4'},
      ],
      'matchChoices': null,
      'savedResponse': {'optionIds': ['o2000000-0000-0000-0000-000000000001'], 'text': null, 'number': null, 'matches': null},
    },
    {
      'examQuestionId': 'q1000000-0000-0000-0000-000000000003',
      'type': 'TrueFalse',
      'stem': 'A quadratic can have two real roots.',
      'marks': 1.0,
      'options': [
        {'id': 'o3000000-0000-0000-0000-000000000001', 'text': 'True'},
        {'id': 'o3000000-0000-0000-0000-000000000002', 'text': 'False'},
      ],
      'matchChoices': null,
      'savedResponse': null,
    },
    {
      'examQuestionId': 'q1000000-0000-0000-0000-000000000004',
      'type': 'Numerical',
      'stem': 'What is 2.5 × 4?',
      'marks': 1.0,
      'options': [],
      'matchChoices': null,
      'savedResponse': {'optionIds': null, 'text': null, 'number': 10, 'matches': null},
    },
    {
      'examQuestionId': 'q1000000-0000-0000-0000-000000000005',
      'type': 'FillBlank',
      'stem': 'The ___ formula solves any quadratic.',
      'marks': 1.0,
      'options': [],
      'matchChoices': null,
      'savedResponse': null,
    },
    {
      'examQuestionId': 'q1000000-0000-0000-0000-000000000006',
      'type': 'Matching',
      'stem': 'Match each term',
      'marks': 2.0,
      'options': [
        {'id': 'o6000000-0000-0000-0000-000000000001', 'text': 'Discriminant'},
        {'id': 'o6000000-0000-0000-0000-000000000002', 'text': 'Vertex'},
      ],
      'matchChoices': ['b² − 4ac', 'Turning point'],
      'savedResponse': {'optionIds': null, 'text': null, 'number': null, 'matches': {'o6000000-0000-0000-0000-000000000002': 'Turning point'}},
    },
    {
      'examQuestionId': 'q1000000-0000-0000-0000-000000000007',
      'type': 'LongAnswer',
      'stem': 'Explain completing the square.',
      'marks': 4.0,
      'options': [],
      'matchChoices': null,
      'savedResponse': null,
    },
  ],
};
