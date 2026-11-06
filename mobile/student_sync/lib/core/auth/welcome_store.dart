import 'package:flutter/foundation.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Remembers that this device has seen the welcome screens.
abstract interface class WelcomeStore {
  Future<bool> seen();
  Future<void> markSeen();
}

class SecureWelcomeStore implements WelcomeStore {
  SecureWelcomeStore([FlutterSecureStorage? storage]) : _storage = storage ?? const FlutterSecureStorage();
  final FlutterSecureStorage _storage;
  static const _key = 'student_sync.welcome_seen.v1';

  @override
  Future<bool> seen() async {
    try {
      return await _storage.read(key: _key) == 'true';
    } on Exception {
      // Unreadable storage must not trap the user on the welcome screens.
      return true;
    }
  }

  @override
  Future<void> markSeen() => _storage.write(key: _key, value: 'true');
}

class InMemoryWelcomeStore implements WelcomeStore {
  InMemoryWelcomeStore({this.initiallySeen = true}) : _seen = initiallySeen;
  final bool initiallySeen;
  bool _seen;

  @override
  Future<bool> seen() async => _seen;

  @override
  Future<void> markSeen() async => _seen = true;
}

/// Whether to show the welcome screens before sign-in. True until [load] says otherwise,
/// so nothing waits on storage.
class WelcomeState extends ValueNotifier<bool> {
  WelcomeState(this._store) : super(true);
  final WelcomeStore _store;

  Future<void> load() async => value = await _store.seen();

  Future<void> complete() async {
    value = true;
    await _store.markSeen();
  }
}
