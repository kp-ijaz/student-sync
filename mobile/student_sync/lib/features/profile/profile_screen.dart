import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:go_router/go_router.dart';
import 'package:image_picker/image_picker.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';
import 'package:timezone/timezone.dart' as tz;

import '../../core/api/api_exception.dart';
import '../../core/app_services.dart';
import '../../core/models/auth_models.dart';
import '../../core/models/enums.dart';
import '../../core/theme/status_styles.dart';
import '../../core/theme/tokens.dart';
import '../../core/time/time_format.dart';
import '../../core/widgets/avatar.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/illustration.dart';
import '../../core/widgets/states.dart';

class ProfileScreen extends StatefulWidget {
  const ProfileScreen({super.key});

  @override
  State<ProfileScreen> createState() => _ProfileScreenState();
}

class _ProfileScreenState extends State<ProfileScreen> {
  bool _saving = false;
  bool _photoBusy = false;

  static const _maxPhotoBytes = 5 * 1000 * 1000;

  /// Take or choose a photo (resized on the device), upload it, or remove the current one.
  Future<void> _editPhoto() async {
    final services = AppScope.read(context);
    final hasPhoto = services.auth.user?.photoUrl != null;
    final choice = await showModalBottomSheet<String>(
      context: context,
      builder: (sheet) => SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(SsSpace.s2, 0, SsSpace.s2, SsSpace.s3),
          child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(SsSpace.s3, 0, SsSpace.s3, SsSpace.s3),
              child: Text('Profile photo', style: Theme.of(sheet).textTheme.titleLarge),
            ),
            SsListRow(icon: LucideIcons.camera, title: 'Take a photo', onTap: () => Navigator.pop(sheet, 'camera'), showChevron: false),
            SsListRow(icon: LucideIcons.images, title: 'Choose from library', onTap: () => Navigator.pop(sheet, 'gallery'), showChevron: false),
            if (hasPhoto)
              SsListRow(icon: LucideIcons.trash2, color: SsColors.error, title: 'Remove photo', onTap: () => Navigator.pop(sheet, 'remove'), showChevron: false),
          ]),
        ),
      ),
    );
    if (choice == null || !mounted) return;
    if (choice == 'remove') return _runPhoto(() => services.profile.removePhoto(), 'Photo removed.');

    final XFile? picked;
    try {
      picked = await ImagePicker().pickImage(
        source: choice == 'camera' ? ImageSource.camera : ImageSource.gallery,
        maxWidth: 1024,
        maxHeight: 1024,
        imageQuality: 85,
        preferredCameraDevice: CameraDevice.front,
      );
    } on PlatformException catch (e) {
      if (mounted) showSnack(context, e.code.contains('denied') ? 'Allow access in Settings to choose a photo.' : "Couldn't open the ${choice == 'camera' ? 'camera' : 'photo library'}.", error: true);
      return;
    }
    if (picked == null || !mounted) return;
    final bytes = await picked.readAsBytes();
    final name = picked.name.toLowerCase();
    final contentType = name.endsWith('.png') ? 'image/png' : 'image/jpeg';
    if (!mounted) return;
    if (bytes.length > _maxPhotoBytes) {
      showSnack(context, 'That photo is larger than 5 MB. Choose a smaller one.', error: true);
      return;
    }
    await _runPhoto(
      () => services.profile.uploadPhoto(bytes: bytes, filename: contentType == 'image/png' ? 'photo.png' : 'photo.jpg', contentType: contentType),
      'Profile photo updated.',
    );
  }

  Future<void> _runPhoto(Future<CurrentUser> Function() action, String done) async {
    final auth = AppScope.read(context).auth;
    setState(() => _photoBusy = true);
    try {
      final updated = await action();
      await auth.updateUser(updated);
      if (mounted) showSnack(context, done);
    } on ApiException catch (e) {
      if (mounted) showSnack(context, e.fieldError('file') ?? e.message, error: true);
    } finally {
      if (mounted) setState(() => _photoBusy = false);
    }
  }

  Future<void> _changeTimeZone() async {
    final services = AppScope.read(context);
    final user = services.auth.user;
    if (user == null) return;
    final chosen = await Navigator.of(context).push<String>(
      MaterialPageRoute(builder: (_) => TimeZonePickerScreen(current: user.timeZoneId), fullscreenDialog: true),
    );
    if (chosen == null || chosen == user.timeZoneId || !mounted) return;
    setState(() => _saving = true);
    try {
      final updated = await services.profile.updateProfile(fullName: user.fullName, timeZoneId: chosen, preferredLanguage: user.preferredLanguage);
      await services.auth.updateUser(updated);
      if (mounted) showSnack(context, 'Time zone updated. Times now show in $chosen.');
    } on ApiException catch (e) {
      if (mounted) showSnack(context, e.fieldError('timeZoneId') ?? e.message, error: true);
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  Future<void> _signOut() async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Sign out?'),
        content: const Text('You will need your email and password to sign in again on this device.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Sign out')),
        ],
      ),
    );
    if (ok == true && mounted) await AppScope.read(context).signOut();
  }

  @override
  Widget build(BuildContext context) {
    final services = AppScope.of(context);
    final t = Theme.of(context).textTheme;
    return ListenableBuilder(
      listenable: services.auth,
      builder: (context, _) {
        final user = services.auth.user;
        if (user == null) return const SizedBox.shrink();
        final now = DateTime.now().toUtc();
        return Scaffold(
          appBar: AppBar(title: const Text('Profile')),
          body: ListView(padding: pagePadding(context), children: [
            const SizedBox(height: SsSpace.s2),
            HeroCard(
              semanticLabel: '${user.fullName}, ${StatusStyles.roleLabel(user.role)}, ${user.email}',
              child: ExcludeSemantics(
                child: Row(children: [
                  _EditablePhoto(name: user.fullName, photoUrl: user.photoUrl, busy: _photoBusy, onTap: _photoBusy ? null : _editPhoto),
                  const SizedBox(width: SsSpace.s4),
                  Expanded(
                    child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                      Text(user.fullName,
                          maxLines: 2,
                          overflow: TextOverflow.ellipsis,
                          style: const TextStyle(fontFamily: 'PlusJakartaSans', fontSize: 19, height: 1.25, fontWeight: FontWeight.w700, color: SsColors.white)),
                      const SizedBox(height: 2),
                      Text(user.email, overflow: TextOverflow.ellipsis, style: const TextStyle(fontFamily: 'Inter', fontSize: 13, color: Color(0xD9FFFFFF))),
                      const SizedBox(height: SsSpace.s2),
                      Container(
                        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                        decoration: BoxDecoration(color: const Color(0x26FFFFFF), borderRadius: BorderRadius.circular(SsRadius.pill)),
                        child: Text(StatusStyles.roleLabel(user.role),
                            style: const TextStyle(fontFamily: 'Inter', fontSize: 12, fontWeight: FontWeight.w700, color: SsColors.white)),
                      ),
                    ]),
                  ),
                ]),
              ),
            ),
            // Students open their progress from here (its own screen).
            if (user.role == UserRole.student) ...[
              const SectionHeader('Learning'),
              SsListGroup(children: [
                SsListRow(
                  icon: LucideIcons.chartLine,
                  color: SsColors.success,
                  title: 'My progress',
                  subtitle: 'Attendance, assignments, exams and more',
                  onTap: () => context.push('/progress'),
                ),
              ]),
            ],
            const SectionHeader('Account'),
            SsListGroup(children: [
              if (user.tenantName != null) SsListRow(icon: LucideIcons.building2, color: SsColors.navy, title: 'Institution', subtitle: user.tenantName!),
              SsListRow(
                icon: LucideIcons.globe,
                title: 'Time zone',
                subtitle: '${user.timeZoneId} (${TimeFormatter.offsetLabel(user.timeZoneId, now)})',
                trailing: _saving ? const SizedBox(width: 20, height: 20, child: CircularProgressIndicator(strokeWidth: 2)) : null,
                showChevron: !_saving,
                onTap: _saving ? null : _changeTimeZone,
              ),
              SsListRow(
                icon: LucideIcons.bell,
                color: SsColors.orangeDeep,
                title: 'Notifications',
                subtitle: 'Messages about classes, work and doubts',
                onTap: () => context.push('/notifications'),
              ),
            ]),
            const SizedBox(height: SsSpace.s3),
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: SsSpace.s1),
              child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                const ExcludeSemantics(child: Icon(LucideIcons.info, size: 16, color: SsColors.slate)),
                const SizedBox(width: SsSpace.s2),
                Expanded(
                  child: Text(
                    services.devices.pushConfigured
                        ? 'Push notifications are enabled for this device.'
                        : 'Push notifications are not set up in this version of the app. New notifications appear in the app when you open it.',
                    style: t.bodySmall,
                  ),
                ),
              ]),
            ),
            const SizedBox(height: SsSpace.s6),
            OutlinedButton.icon(
              onPressed: _signOut,
              icon: const Icon(LucideIcons.logOut, size: 20),
              label: const Text('Sign out'),
              style: OutlinedButton.styleFrom(
                foregroundColor: SsColors.error,
                side: BorderSide(color: SsColors.error.withValues(alpha: 0.3)),
                minimumSize: const Size.fromHeight(52),
              ),
            ),
            const SizedBox(height: SsSpace.s4),
            Text('Connected to ${services.config.origin}', style: t.labelSmall, textAlign: TextAlign.center),
          ]),
        );
      },
    );
  }
}

/// The profile photo with a camera badge; tapping it opens the photo options.
class _EditablePhoto extends StatelessWidget {
  const _EditablePhoto({required this.name, required this.photoUrl, required this.busy, required this.onTap});
  final String name;
  final String? photoUrl;
  final bool busy;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) => Semantics(
        button: true,
        label: photoUrl == null ? 'Add a profile photo' : 'Change profile photo',
        excludeSemantics: true,
        child: GestureDetector(
          onTap: onTap,
          child: Stack(clipBehavior: Clip.none, children: [
            UserAvatar(name: name, photoUrl: photoUrl, size: 76),
            if (busy)
              const Positioned.fill(
                child: DecoratedBox(
                  decoration: BoxDecoration(shape: BoxShape.circle, color: Color(0x880B2854)),
                  child: Center(child: SizedBox(width: 24, height: 24, child: CircularProgressIndicator(strokeWidth: 2.4, color: SsColors.white))),
                ),
              ),
            Positioned(
              right: -2,
              bottom: -2,
              child: Container(
                width: 28,
                height: 28,
                decoration: BoxDecoration(color: SsColors.orange, shape: BoxShape.circle, border: Border.all(color: SsColors.white, width: 2)),
                child: const Icon(LucideIcons.camera, size: 14, color: SsColors.navy),
              ),
            ),
          ]),
        ),
      );
}

/// Searchable list of IANA time zones, each with its current UTC offset.
class TimeZonePickerScreen extends StatefulWidget {
  const TimeZonePickerScreen({super.key, required this.current});
  final String current;

  @override
  State<TimeZonePickerScreen> createState() => _TimeZonePickerScreenState();
}

class _TimeZonePickerScreenState extends State<TimeZonePickerScreen> {
  late final List<String> _all = tz.timeZoneDatabase.locations.keys
      .where((k) => k.contains('/') && !k.startsWith('Etc/') && !k.startsWith('SystemV/'))
      .toList()
    ..sort();
  String _query = '';

  @override
  Widget build(BuildContext context) {
    final now = DateTime.now().toUtc();
    final q = _query.toLowerCase().replaceAll(' ', '_');
    final items = q.isEmpty ? _all : _all.where((z) => z.toLowerCase().contains(q)).toList();
    return Scaffold(
      appBar: AppBar(title: const Text('Choose time zone')),
      body: Column(children: [
        Padding(
          padding: const EdgeInsets.all(SsSpace.s4),
          child: TextField(
            autofocus: true,
            decoration: const InputDecoration(labelText: 'Search city or region', prefixIcon: Icon(LucideIcons.search), hintText: 'e.g. Dubai, London'),
            onChanged: (v) => setState(() => _query = v.trim()),
          ),
        ),
        Expanded(
          child: items.isEmpty
              ? const EmptyState(icon: LucideIcons.searchX, illustration: Illustration.search, title: 'No matching time zones', message: 'Try a city name, like Dubai or London.')
              : ListView.builder(
                  itemCount: items.length,
                  itemBuilder: (context, i) {
                    final z = items[i];
                    final selected = z == widget.current;
                    return ListTile(
                      selected: selected,
                      selectedTileColor: SsColors.blue50,
                      title: Text(z.replaceAll('_', ' ')),
                      subtitle: Text(TimeFormatter.offsetLabel(z, now)),
                      trailing: selected ? const Icon(LucideIcons.check, color: SsColors.blue, semanticLabel: 'Current time zone') : null,
                      onTap: () => Navigator.of(context).pop(z),
                    );
                  },
                ),
        ),
      ]),
    );
  }
}
