import 'dart:async';
import 'dart:ui' as ui;

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:http/http.dart' as http;
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../app_services.dart';
import '../theme/tokens.dart';

/// A person's photo in a circle, or their initials on the brand gradient when there
/// is no photo (or it cannot be loaded). [photoUrl] is the server path from the API.
class UserAvatar extends StatelessWidget {
  const UserAvatar({super.key, required this.name, this.photoUrl, this.size = 40, this.ring = true});
  final String name;
  final String? photoUrl;
  final double size;

  /// White ring around the circle, for avatars on coloured or photo backgrounds.
  final bool ring;

  static String initials(String name) {
    // Ignore bracketed suffixes such as "(Demo Student)".
    final parts = name.replaceAll(RegExp(r'\([^)]*\)'), ' ').trim().split(RegExp(r'\s+')).where((p) => p.isNotEmpty).toList();
    if (parts.isEmpty) return '?';
    final first = parts.first.characters.first;
    final last = parts.length > 1 ? parts.last.characters.first : '';
    return (first + last).toUpperCase();
  }

  @override
  Widget build(BuildContext context) {
    final fallback = _Initials(name: name, size: size);
    final url = photoUrl;
    return ExcludeSemantics(
      child: Container(
        width: size,
        height: size,
        padding: EdgeInsets.all(ring ? size / 22 : 0),
        decoration: BoxDecoration(shape: BoxShape.circle, color: SsColors.white, boxShadow: ring ? SsShadow.card : null),
        child: ClipOval(
          child: url == null
              ? fallback
              : Image(
                  image: PersonPhoto.fromServerPath(context, url),
                  fit: BoxFit.cover,
                  gaplessPlayback: true,
                  frameBuilder: (context, child, frame, sync) => frame == null && !sync ? fallback : child,
                  errorBuilder: (_, _, _) => fallback,
                ),
        ),
      ),
    );
  }
}

/// Portrait card for a teacher: their photo (or initials) filling the card, with name
/// and subject over a dark fade and an arrow button. Used for "Your teachers".
class TeacherPhotoCard extends StatelessWidget {
  const TeacherPhotoCard({super.key, required this.name, this.photoUrl, this.subtitle, this.onTap});
  final String name;
  final String? photoUrl;
  final String? subtitle;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final radius = BorderRadius.circular(SsRadius.card);
    final url = photoUrl;
    final fallback = _Initials(name: name, size: 72);
    return Semantics(
      button: onTap != null,
      label: [name, ?subtitle].join(', '),
      excludeSemantics: true,
      child: DecoratedBox(
        decoration: BoxDecoration(borderRadius: radius, boxShadow: SsShadow.card),
        child: ClipRRect(
          borderRadius: radius,
          child: Material(
            type: MaterialType.transparency,
            child: InkWell(
              onTap: onTap,
              child: Stack(fit: StackFit.expand, children: [
                if (url == null)
                  fallback
                else
                  Image(
                    image: PersonPhoto.fromServerPath(context, url),
                    fit: BoxFit.cover,
                    frameBuilder: (context, child, frame, sync) => frame == null && !sync ? fallback : child,
                    errorBuilder: (_, _, _) => fallback,
                  ),
                const DecoratedBox(
                  decoration: BoxDecoration(
                    gradient: LinearGradient(
                      begin: Alignment.topCenter,
                      end: Alignment.bottomCenter,
                      colors: [Color(0x000B2854), Color(0xD90B2854)],
                      stops: [0.45, 1],
                    ),
                  ),
                ),
                Positioned(
                  left: SsSpace.s3,
                  right: 52,
                  bottom: SsSpace.s3,
                  child: Column(crossAxisAlignment: CrossAxisAlignment.start, mainAxisSize: MainAxisSize.min, children: [
                    Text(_shortName(name),
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: const TextStyle(fontFamily: 'PlusJakartaSans', fontSize: 15, fontWeight: FontWeight.w700, color: SsColors.white)),
                    if (subtitle != null)
                      Text(subtitle!,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: const TextStyle(fontFamily: 'Inter', fontSize: 12, color: Color(0xD9FFFFFF))),
                  ]),
                ),
                if (onTap != null)
                  Positioned(
                    right: SsSpace.s3,
                    bottom: SsSpace.s3,
                    child: Container(
                      width: 32,
                      height: 32,
                      decoration: BoxDecoration(color: SsColors.white, borderRadius: BorderRadius.circular(10)),
                      child: const Icon(LucideIcons.arrowUpRight, size: 18, color: SsColors.navy),
                    ),
                  ),
              ]),
            ),
          ),
        ),
      ),
    );
  }

  /// "Amira Haddad (Demo Teacher)" → "Amira Haddad".
  static String _shortName(String name) => name.replaceAll(RegExp(r'\s*\([^)]*\)'), '').trim();
}

class _Initials extends StatelessWidget {
  const _Initials({required this.name, required this.size});
  final String name;
  final double size;

  @override
  Widget build(BuildContext context) => DecoratedBox(
        decoration: const BoxDecoration(gradient: SsGradients.brand),
        child: Center(
          child: Text(
            UserAvatar.initials(name),
            style: TextStyle(fontFamily: 'PlusJakartaSans', fontWeight: FontWeight.w700, fontSize: size * 0.36, color: SsColors.white, letterSpacing: 0.3),
          ),
        ),
      );
}

/// Image provider for a profile photo. The API hands out short-lived download links
/// that change on every response, so the cache key is the photo's id (the `v` query
/// parameter) rather than the link: a photo is fetched once per app run.
@immutable
class PersonPhoto extends ImageProvider<PersonPhoto> {
  const PersonPhoto(this.url, this.cacheId);

  factory PersonPhoto.fromServerPath(BuildContext context, String path) {
    final url = AppScope.of(context).config.resolveServerPath(path);
    return PersonPhoto(url, url.queryParameters['v'] ?? url.toString());
  }

  final Uri url;
  final String cacheId;

  @override
  Future<PersonPhoto> obtainKey(ImageConfiguration configuration) => SynchronousFuture(this);

  @override
  ImageStreamCompleter loadImage(PersonPhoto key, ImageDecoderCallback decode) =>
      MultiFrameImageStreamCompleter(codec: _load(decode), scale: 1, debugLabel: 'PersonPhoto($cacheId)');

  Future<ui.Codec> _load(ImageDecoderCallback decode) async {
    final response = await http.get(url).timeout(const Duration(seconds: 20));
    if (response.statusCode != 200 || response.bodyBytes.isEmpty) {
      // Do not keep a failed load in the cache; the next build retries.
      scheduleMicrotask(() => PaintingBinding.instance.imageCache.evict(this));
      throw StateError('Photo unavailable (${response.statusCode})');
    }
    return decode(await ui.ImmutableBuffer.fromUint8List(response.bodyBytes));
  }

  @override
  bool operator ==(Object other) => other is PersonPhoto && other.cacheId == cacheId;

  @override
  int get hashCode => cacheId.hashCode;
}
