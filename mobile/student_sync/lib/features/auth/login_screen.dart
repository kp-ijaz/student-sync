import 'package:flutter/material.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/api/api_exception.dart';
import '../../core/app_services.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/tokens.dart';
import '../../core/widgets/states.dart';
import 'auth_shell.dart';

final _emailPattern = RegExp(r'^[^@\s]+@[^@\s]+\.[^@\s]+$');

String? validateEmail(String? value) {
  final v = value?.trim() ?? '';
  if (v.isEmpty) return 'Enter your email address.';
  if (!_emailPattern.hasMatch(v)) return 'Enter a valid email address, like name@example.com.';
  return null;
}

String? validatePasswordPresent(String? value) => (value == null || value.isEmpty) ? 'Enter your password.' : null;

class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key});

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _form = GlobalKey<FormState>();
  final _email = TextEditingController();
  final _password = TextEditingController();
  final _institution = TextEditingController();
  bool _showInstitution = false;
  bool _obscure = true;
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _email.dispose();
    _password.dispose();
    _institution.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    FocusScope.of(context).unfocus();
    setState(() => _error = null);
    if (!(_form.currentState?.validate() ?? false)) return;
    if (_showInstitution && _institution.text.trim().isEmpty) {
      setState(() => _error = 'Enter your institution code.');
      return;
    }
    setState(() => _busy = true);
    try {
      await AppScope.read(context).auth.signIn(
            email: _email.text,
            password: _password.text,
            institutionCode: _showInstitution ? _institution.text : null,
          );
      // The router redirects on success.
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        if (e.code == 'institution_code_required') {
          _showInstitution = true;
          _error = e.message;
        } else {
          _error = e.message;
        }
      });
    } on Object catch (e) {
      if (mounted) setState(() => _error = describeError(e));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).textTheme;
    final auth = AppScope.of(context).auth;
    final expired = auth.lastSignOutReason == SignOutReason.sessionExpired;
    return AuthShell(
      child: AutofillGroup(
        child: Form(
          key: _form,
          child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
            Semantics(header: true, child: Text('Welcome back', style: t.headlineMedium)),
            const SizedBox(height: SsSpace.s1),
            Text('Sign in with the email and password your institute gave you.', style: t.bodyMedium?.copyWith(color: SsColors.textBody)),
            const SizedBox(height: SsSpace.s5),
            if (expired && _error == null) ...[
              const AuthBanner(message: 'Your session has expired. Please sign in again.', color: SsColors.warning, icon: LucideIcons.clock),
              const SizedBox(height: SsSpace.s4),
            ],
            if (_error != null) ...[
              AuthBanner(message: _error!, color: SsColors.error, icon: LucideIcons.circleAlert),
              const SizedBox(height: SsSpace.s4),
            ],
            TextFormField(
              key: const Key('login.email'),
              controller: _email,
              decoration: const InputDecoration(labelText: 'Email', hintText: 'name@example.com', prefixIcon: Icon(LucideIcons.mail)),
              keyboardType: TextInputType.emailAddress,
              autofillHints: const [AutofillHints.username, AutofillHints.email],
              textInputAction: TextInputAction.next,
              autocorrect: false,
              enabled: !_busy,
              validator: validateEmail,
            ),
            const SizedBox(height: SsSpace.s4),
            TextFormField(
              key: const Key('login.password'),
              controller: _password,
              decoration: InputDecoration(
                labelText: 'Password',
                prefixIcon: const Icon(LucideIcons.lock),
                suffixIcon: IconButton(
                  tooltip: _obscure ? 'Show password' : 'Hide password',
                  icon: Icon(_obscure ? LucideIcons.eye : LucideIcons.eyeOff),
                  onPressed: () => setState(() => _obscure = !_obscure),
                ),
              ),
              obscureText: _obscure,
              autofillHints: const [AutofillHints.password],
              textInputAction: _showInstitution ? TextInputAction.next : TextInputAction.done,
              enabled: !_busy,
              validator: validatePasswordPresent,
              onFieldSubmitted: (_) => _showInstitution ? null : _submit(),
            ),
            // Only when the server asks for it (an email shared across institutions).
            AnimatedSize(
              duration: const Duration(milliseconds: 220),
              curve: Curves.easeOutCubic,
              child: _showInstitution
                  ? Padding(
                      padding: const EdgeInsets.only(top: SsSpace.s4),
                      child: TextFormField(
                        key: const Key('login.institution'),
                        controller: _institution,
                        decoration: const InputDecoration(
                          labelText: 'Institution code',
                          prefixIcon: Icon(LucideIcons.building2),
                          helperText: 'Your email is registered at more than one institution. Ask your centre for its code.',
                          helperMaxLines: 2,
                        ),
                        textInputAction: TextInputAction.done,
                        autocorrect: false,
                        enabled: !_busy,
                        onFieldSubmitted: (_) => _submit(),
                      ),
                    )
                  : const SizedBox(width: double.infinity),
            ),
            const SizedBox(height: SsSpace.s5),
            FilledButton(
              key: const Key('login.submit'),
              style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(52)),
              onPressed: _busy ? null : _submit,
              child: AnimatedSwitcher(
                duration: const Duration(milliseconds: 180),
                child: _busy
                    ? const SizedBox(height: 22, width: 22, child: CircularProgressIndicator(strokeWidth: 2.4, color: SsColors.white, semanticsLabel: 'Signing in'))
                    : const Text('Sign in'),
              ),
            ),
            const SizedBox(height: SsSpace.s5),
            Text(
              'New students receive their sign-in details from the institute office. Forgot your password? Ask the office to reset it.',
              style: t.bodySmall?.copyWith(color: SsColors.textBody),
              textAlign: TextAlign.center,
            ),
          ]),
        ),
      ),
    );
  }
}
