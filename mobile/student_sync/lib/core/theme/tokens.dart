import 'package:flutter/material.dart';

/// Brand tokens. Mirrors DESIGN.md §1 and `src/StudentSync.Web/wwwroot/css/tokens.css`.
/// Keep both in sync when a token changes. Mobile-only tokens are marked (DESIGN.md §9).
abstract final class SsColors {
  static const navy = Color(0xFF0B2854);
  static const blue = Color(0xFF1264D8);
  static const orange = Color(0xFFFF9F24);
  static const white = Color(0xFFFFFFFF);
  static const background = Color(0xFFF3F6FC);
  static const slate = Color(0xFF64748B);
  static const success = Color(0xFF16A36A);
  static const warning = Color(0xFFF59E0B);
  static const error = Color(0xFFE5484D);
  static const info = Color(0xFF0EA5E9);
  static const border = Color(0xFFE2E8F0);
  static const blue50 = Color(0xFFEAF2FF);
  static const orange50 = Color(0xFFFFF4E5);

  static const textPrimary = Color(0xFF0B2854);
  static const textBody = Color(0xFF1E293B);
  static const textSecondary = Color(0xFF64748B);

  // Mobile-only: a deeper blue for gradient ends, a hairline for borderless cards and
  // the fill for inputs and grouped rows.
  static const blueDeep = Color(0xFF0A4BB0);
  static const hairline = Color(0x140B2854);
  static const fill = Color(0xFFEFF3FA);

  // Mobile-only: AA-contrast oranges for icons and text on light surfaces
  // (brand orange itself is too light for either).
  static const orangeDeep = Color(0xFFC2410C);
  static const orangeText = Color(0xFF9A5B00);
}

/// Spacing scale (DESIGN.md §3): 4, 8, 12, 16, 24, 32, 48, 64. No other values.
abstract final class SsSpace {
  static const s1 = 4.0;
  static const s2 = 8.0;
  static const s3 = 12.0;
  static const s4 = 16.0;
  static const s5 = 24.0;
  static const s6 = 32.0;
  static const s7 = 48.0;
  static const s8 = 64.0;
}

/// Mobile radii (DESIGN.md §9): softer than the web so cards read as touch surfaces.
abstract final class SsRadius {
  static const card = 20.0;
  static const control = 14.0;
  static const sheet = 28.0;
  static const pill = 999.0;
}

abstract final class SsShadow {
  /// Soft, diffuse navy shadow for cards (never coloured or glowing).
  static const card = [
    BoxShadow(color: Color(0x0A0B2854), blurRadius: 2, offset: Offset(0, 1)),
    BoxShadow(color: Color(0x0F0B2854), blurRadius: 24, offset: Offset(0, 8)),
  ];

  /// Floating surfaces: the bottom navigation bar and sheets.
  static const floating = [
    BoxShadow(color: Color(0x140B2854), blurRadius: 32, offset: Offset(0, 12)),
    BoxShadow(color: Color(0x0A0B2854), blurRadius: 4, offset: Offset(0, 1)),
  ];
}

abstract final class SsGradients {
  /// Navy → blue, used for the sign-in hero and each role's home hero card.
  static const brand = LinearGradient(colors: [SsColors.navy, SsColors.blueDeep, SsColors.blue], stops: [0, 0.55, 1], begin: Alignment.topLeft, end: Alignment.bottomRight);
}

/// Motion (DESIGN.md §7): short ease-out transitions.
abstract final class SsMotion {
  static const fast = Duration(milliseconds: 180);
  static const medium = Duration(milliseconds: 260);
  static const curve = Curves.easeOutCubic;
}

/// Minimum touch target (DESIGN.md §6).
const double kMinTouchTarget = 44;
