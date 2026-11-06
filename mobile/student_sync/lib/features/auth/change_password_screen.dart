import 'package:flutter/material.dart';

import '../../core/api/api_exception.dart';
import '../../core/app_services.dart';
import '../../core/theme/tokens.dart';
import '../../core/widgets/states.dart';

/// Mirrors the server policy: ≥10 characters with upper case, lower case and a digit.
String? validateNewPassword(String? value) {
  final v = value ?? '';
  if (v.length < 10) return 'Use at least 10 characters.';
  if (!v.contains(RegExp('[A-Z]'))) return 'Include at least one upper-case letter.';
  if (!v.contains(RegExp('[a-z]'))) return 'Include at least one lower-case letter.';
  if (!v.contains(RegExp('[0-9]'))) return 'Include at least one number.';
  return null;
}

/// Shown when the account has a temporary password. Every other API call is
/// blocked by the server (`password_change_required`) until this succeeds.
class ChangePasswordScreen extends StatefulWidget {
  const ChangePasswordScreen({super.key});

  @override
  State<ChangePasswordScreen> createState() => _ChangePasswordScreenState();
}

class _ChangePasswordScreenState extends State<ChangePasswordScreen> {
  final _form = GlobalKey<FormState>();
  final _current = TextEditingController();
  final _next = TextEditingController();
  final _confirm = TextEditingController();
  bool _busy = false;
  String? _error;
  String? _newPasswordServerError;

  @override
  void dispose() {
    _current.dispose();
    _next.dispose();
    _confirm.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    setState(() {
      _error = null;
      _newPasswordServerError = null;
    });
    if (!(_form.currentState?.validate() ?? false)) return;
    setState(() => _busy = true);
    try {
      await AppScope.read(context).auth.changePassword(currentPassword: _current.text, newPassword: _next.text);
      if (mounted) showSnack(context, 'Password changed. You are signed in.');
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _newPasswordServerError = e.fieldError('newPassword');
        _error = _newPasswordServerError == null ? e.message : null;
      });
      _form.currentState?.validate();
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    final t = Theme.of(context).textTheme;
    return Scaffold(
      appBar: AppBar(
        title: const Text('Set a new password'),
        actions: [TextButton(onPressed: _busy ? null : services.signOut, child: const Text('Sign out'))],
      ),
      body: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(SsSpace.s5),
          child: Form(
            key: _form,
            child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
              Text(
                'Your account is using a temporary password. Choose a new one to continue.',
                style: t.bodyMedium,
              ),
              const SizedBox(height: SsSpace.s5),
              if (_error != null) ...[
                Semantics(liveRegion: true, child: Text(_error!, style: t.bodyMedium?.copyWith(color: SsColors.error))),
                const SizedBox(height: SsSpace.s4),
              ],
              TextFormField(
                controller: _current,
                obscureText: true,
                decoration: const InputDecoration(labelText: 'Current (temporary) password'),
                autofillHints: const [AutofillHints.password],
                validator: (v) => (v == null || v.isEmpty) ? 'Enter your current password.' : null,
              ),
              const SizedBox(height: SsSpace.s4),
              TextFormField(
                controller: _next,
                obscureText: true,
                decoration: const InputDecoration(
                  labelText: 'New password',
                  helperText: 'At least 10 characters, with upper- and lower-case letters and a number.',
                ),
                autofillHints: const [AutofillHints.newPassword],
                validator: (v) => _newPasswordServerError ?? validateNewPassword(v),
              ),
              const SizedBox(height: SsSpace.s4),
              TextFormField(
                controller: _confirm,
                obscureText: true,
                decoration: const InputDecoration(labelText: 'Confirm new password'),
                validator: (v) => v != _next.text ? 'The passwords do not match.' : null,
                onFieldSubmitted: (_) => _submit(),
              ),
              const SizedBox(height: SsSpace.s5),
              FilledButton(
                onPressed: _busy ? null : _submit,
                child: _busy
                    ? const SizedBox(height: 20, width: 20, child: CircularProgressIndicator(strokeWidth: 2, color: SsColors.white))
                    : const Text('Change password'),
              ),
            ]),
          ),
        ),
      ),
    );
  }
}
