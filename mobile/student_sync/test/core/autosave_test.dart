import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:student_sync/core/models/learning_models.dart';
import 'package:student_sync/features/learning/exam_attempt_screen.dart';
import 'package:student_sync/features/learning/learning_api.dart';

import '../support/fake_backend.dart';

void main() {
  group('AnswerAutosaver', () {
    late TestHarness h;
    late List<Map<String, dynamic>> saved;

    setUp(() async {
      h = await TestHarness.signedIn();
      saved = [];
      h.backend.onApi = (req, _) {
        if (req.method == 'PUT' && req.url.path.endsWith('/answers')) {
          saved.add(jsonDecode(req.body) as Map<String, dynamic>);
          return http.Response('', 204);
        }
        return http.Response('', 404);
      };
    });

    test('debounces typing and only sends the latest text', () async {
      final saver = AnswerAutosaver(attemptId: 'att', api: LearningApi(h.api), debounce: const Duration(milliseconds: 30));
      saver.update('q1', const ExamResponse(text: 'q'));
      saver.update('q1', const ExamResponse(text: 'qua'));
      saver.update('q1', const ExamResponse(text: 'quadratic'));
      await Future<void>.delayed(const Duration(milliseconds: 120));
      expect(saved, hasLength(1));
      expect(saved.single, {
        'examQuestionId': 'q1',
        'response': {'optionIds': null, 'text': 'quadratic', 'number': null, 'matches': null},
      });
      expect(saver.states['q1'], SaveState.saved);
      expect(saver.hasUnsaved, isFalse);
    });

    test('choices save immediately and flush() completes pending text', () async {
      final saver = AnswerAutosaver(attemptId: 'att', api: LearningApi(h.api), debounce: const Duration(seconds: 10));
      saver.update('q1', const ExamResponse(optionIds: ['o1']), immediate: true);
      saver.update('q2', const ExamResponse(text: 'draft'));
      await saver.flush();
      expect(saved.map((s) => s['examQuestionId']), containsAll(['q1', 'q2']));
      expect(saver.hasUnsaved, isFalse);
    });

    test('a failed save is kept for retry and reported', () async {
      h.backend.onApi = (req, _) => FakeBackend.problem(422, 'attempt_expired', 'Time is up for this attempt.');
      final saver = AnswerAutosaver(attemptId: 'att', api: LearningApi(h.api));
      saver.update('q1', const ExamResponse(number: 4), immediate: true);
      await saver.flush();
      expect(saver.states['q1'], SaveState.failed);
      expect(saver.lastError?.message, 'Time is up for this attempt.');
      expect(saver.hasUnsaved, isTrue);
    });
  });
}
