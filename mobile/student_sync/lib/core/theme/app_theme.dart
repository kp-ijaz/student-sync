import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'tokens.dart';

const _heading = 'PlusJakartaSans';
const _body = 'Inter';

/// Material 3 theme built from the Student Sync tokens (DESIGN.md §1–3, §9).
/// Dark mode is not shipped in v1 (documented limitation in DESIGN.md).
ThemeData buildAppTheme() {
  const scheme = ColorScheme(
    brightness: Brightness.light,
    primary: SsColors.blue,
    onPrimary: SsColors.white,
    primaryContainer: SsColors.blue50,
    onPrimaryContainer: SsColors.navy,
    secondary: SsColors.navy,
    onSecondary: SsColors.white,
    secondaryContainer: SsColors.blue50,
    onSecondaryContainer: SsColors.navy,
    tertiary: SsColors.orange,
    onTertiary: SsColors.navy,
    tertiaryContainer: SsColors.orange50,
    onTertiaryContainer: SsColors.navy,
    error: SsColors.error,
    onError: SsColors.white,
    surface: SsColors.white,
    onSurface: SsColors.textBody,
    onSurfaceVariant: SsColors.textSecondary,
    outline: SsColors.border,
    outlineVariant: SsColors.border,
    surfaceContainerLowest: SsColors.white,
    surfaceContainerLow: SsColors.background,
    surfaceContainer: SsColors.background,
    surfaceContainerHigh: SsColors.white,
    surfaceContainerHighest: SsColors.fill,
  );

  // Type scale (px / line-height): Display 28/36 · H1 24/32 · H2 20/28 · H3 16/24 ·
  // Body 14/22 · Small 13/20 · Caption 12/16. Headings tighten their tracking slightly.
  const textTheme = TextTheme(
    displaySmall: TextStyle(fontFamily: _heading, fontSize: 28, height: 36 / 28, fontWeight: FontWeight.w700, letterSpacing: -0.6, color: SsColors.textPrimary),
    headlineMedium: TextStyle(fontFamily: _heading, fontSize: 24, height: 32 / 24, fontWeight: FontWeight.w700, letterSpacing: -0.4, color: SsColors.textPrimary),
    headlineSmall: TextStyle(fontFamily: _heading, fontSize: 20, height: 28 / 20, fontWeight: FontWeight.w700, letterSpacing: -0.3, color: SsColors.textPrimary),
    titleLarge: TextStyle(fontFamily: _heading, fontSize: 20, height: 28 / 20, fontWeight: FontWeight.w700, letterSpacing: -0.3, color: SsColors.textPrimary),
    titleMedium: TextStyle(fontFamily: _heading, fontSize: 16, height: 24 / 16, fontWeight: FontWeight.w700, letterSpacing: -0.1, color: SsColors.textPrimary),
    titleSmall: TextStyle(fontFamily: _body, fontSize: 14, height: 22 / 14, fontWeight: FontWeight.w600, color: SsColors.textPrimary),
    bodyLarge: TextStyle(fontFamily: _body, fontSize: 16, height: 24 / 16, fontWeight: FontWeight.w400, color: SsColors.textBody),
    bodyMedium: TextStyle(fontFamily: _body, fontSize: 14, height: 22 / 14, fontWeight: FontWeight.w400, color: SsColors.textBody),
    bodySmall: TextStyle(fontFamily: _body, fontSize: 13, height: 20 / 13, fontWeight: FontWeight.w400, color: SsColors.textSecondary),
    labelLarge: TextStyle(fontFamily: _body, fontSize: 15, height: 20 / 15, fontWeight: FontWeight.w600),
    labelMedium: TextStyle(fontFamily: _body, fontSize: 13, height: 20 / 13, fontWeight: FontWeight.w500),
    labelSmall: TextStyle(fontFamily: _body, fontSize: 12, height: 16 / 12, fontWeight: FontWeight.w500, color: SsColors.textSecondary),
  );

  final controlRadius = BorderRadius.circular(SsRadius.control);
  final controlShape = RoundedRectangleBorder(borderRadius: controlRadius);
  const buttonSize = Size(kMinTouchTarget, 48);
  const buttonPadding = EdgeInsets.symmetric(horizontal: SsSpace.s5, vertical: SsSpace.s3);
  OutlineInputBorder inputBorder(Color color, [double width = 1]) =>
      OutlineInputBorder(borderRadius: controlRadius, borderSide: BorderSide(color: color, width: width));

  return ThemeData(
    useMaterial3: true,
    colorScheme: scheme,
    fontFamily: _body,
    textTheme: textTheme,
    scaffoldBackgroundColor: SsColors.background,
    materialTapTargetSize: MaterialTapTargetSize.padded,
    visualDensity: VisualDensity.standard,
    splashFactory: InkSparkle.splashFactory,
    // The app bar sits on the page background and only gains a hairline once content
    // scrolls beneath it, so headers feel part of the page rather than a separate bar.
    appBarTheme: const AppBarTheme(
      backgroundColor: SsColors.background,
      foregroundColor: SsColors.navy,
      surfaceTintColor: Colors.transparent,
      elevation: 0,
      scrolledUnderElevation: 0.5,
      shadowColor: SsColors.hairline,
      centerTitle: false,
      titleSpacing: SsSpace.s4,
      toolbarHeight: 64,
      systemOverlayStyle: SystemUiOverlayStyle(
        statusBarColor: Colors.transparent,
        statusBarIconBrightness: Brightness.dark,
        statusBarBrightness: Brightness.light,
      ),
      titleTextStyle: TextStyle(fontFamily: _heading, fontSize: 22, height: 28 / 22, fontWeight: FontWeight.w700, letterSpacing: -0.4, color: SsColors.navy),
      iconTheme: IconThemeData(color: SsColors.navy, size: 22),
      actionsIconTheme: IconThemeData(color: SsColors.navy, size: 22),
    ),
    iconTheme: const IconThemeData(color: SsColors.navy, size: 22),
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(
        minimumSize: buttonSize,
        shape: controlShape,
        padding: buttonPadding,
        elevation: 0,
        textStyle: textTheme.labelLarge,
        disabledBackgroundColor: SsColors.fill,
        disabledForegroundColor: SsColors.slate,
      ),
    ),
    elevatedButtonTheme: ElevatedButtonThemeData(
      style: ElevatedButton.styleFrom(minimumSize: buttonSize, shape: controlShape, padding: buttonPadding, textStyle: textTheme.labelLarge),
    ),
    outlinedButtonTheme: OutlinedButtonThemeData(
      style: OutlinedButton.styleFrom(
        minimumSize: buttonSize,
        shape: controlShape,
        foregroundColor: SsColors.navy,
        backgroundColor: SsColors.white,
        side: const BorderSide(color: SsColors.border),
        padding: buttonPadding,
        textStyle: textTheme.labelLarge,
      ),
    ),
    textButtonTheme: TextButtonThemeData(
      style: TextButton.styleFrom(minimumSize: const Size(kMinTouchTarget, kMinTouchTarget), shape: controlShape, foregroundColor: SsColors.blue, textStyle: textTheme.labelLarge),
    ),
    iconButtonTheme: IconButtonThemeData(
      style: IconButton.styleFrom(minimumSize: const Size(kMinTouchTarget, kMinTouchTarget), foregroundColor: SsColors.navy),
    ),
    floatingActionButtonTheme: FloatingActionButtonThemeData(
      backgroundColor: SsColors.blue,
      foregroundColor: SsColors.white,
      elevation: 2,
      highlightElevation: 4,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(SsRadius.card)),
      extendedTextStyle: textTheme.labelLarge,
    ),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: SsColors.fill,
      contentPadding: const EdgeInsets.symmetric(horizontal: SsSpace.s4, vertical: SsSpace.s4),
      border: inputBorder(Colors.transparent),
      enabledBorder: inputBorder(Colors.transparent),
      disabledBorder: inputBorder(Colors.transparent),
      focusedBorder: inputBorder(SsColors.blue, 1.6),
      errorBorder: inputBorder(SsColors.error),
      focusedErrorBorder: inputBorder(SsColors.error, 1.6),
      labelStyle: const TextStyle(color: SsColors.textSecondary, fontWeight: FontWeight.w500),
      floatingLabelStyle: const TextStyle(color: SsColors.navy, fontWeight: FontWeight.w600),
      hintStyle: const TextStyle(color: SsColors.slate),
      prefixIconColor: SsColors.slate,
      suffixIconColor: SsColors.slate,
      floatingLabelBehavior: FloatingLabelBehavior.auto,
      helperMaxLines: 3,
      errorMaxLines: 3,
    ),
    cardTheme: CardThemeData(
      color: SsColors.white,
      surfaceTintColor: Colors.transparent,
      elevation: 0,
      margin: EdgeInsets.zero,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(SsRadius.card), side: const BorderSide(color: SsColors.hairline)),
    ),
    dividerTheme: const DividerThemeData(color: SsColors.border, thickness: 1, space: 1),
    chipTheme: ChipThemeData(
      backgroundColor: SsColors.white,
      selectedColor: SsColors.blue50,
      side: const BorderSide(color: SsColors.border),
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(SsRadius.pill)),
      labelStyle: const TextStyle(fontFamily: _body, fontSize: 13, fontWeight: FontWeight.w500, color: SsColors.textBody),
    ),
    // Pill-shaped segmented controls: a white thumb on a soft track.
    segmentedButtonTheme: SegmentedButtonThemeData(
      style: ButtonStyle(
        minimumSize: const WidgetStatePropertyAll(Size(0, kMinTouchTarget)),
        visualDensity: VisualDensity.standard,
        shape: const WidgetStatePropertyAll(StadiumBorder()),
        side: const WidgetStatePropertyAll(BorderSide(color: SsColors.border)),
        textStyle: const WidgetStatePropertyAll(TextStyle(fontFamily: _body, fontSize: 14, fontWeight: FontWeight.w600)),
        backgroundColor: WidgetStateProperty.resolveWith((s) => s.contains(WidgetState.selected) ? SsColors.navy : SsColors.white),
        foregroundColor: WidgetStateProperty.resolveWith((s) => s.contains(WidgetState.selected) ? SsColors.white : SsColors.textBody),
        iconColor: WidgetStateProperty.resolveWith((s) => s.contains(WidgetState.selected) ? SsColors.white : SsColors.slate),
      ),
    ),
    // Tabs render as a filled pill under the active label.
    tabBarTheme: TabBarThemeData(
      labelColor: SsColors.white,
      unselectedLabelColor: SsColors.textBody,
      indicatorSize: TabBarIndicatorSize.tab,
      indicator: BoxDecoration(color: SsColors.navy, borderRadius: BorderRadius.circular(SsRadius.pill)),
      dividerColor: Colors.transparent,
      dividerHeight: 0,
      overlayColor: const WidgetStatePropertyAll(Colors.transparent),
      splashFactory: NoSplash.splashFactory,
      labelPadding: const EdgeInsets.symmetric(horizontal: SsSpace.s4),
      labelStyle: const TextStyle(fontFamily: _body, fontSize: 14, fontWeight: FontWeight.w600),
      unselectedLabelStyle: const TextStyle(fontFamily: _body, fontSize: 14, fontWeight: FontWeight.w500),
      tabAlignment: TabAlignment.start,
    ),
    snackBarTheme: SnackBarThemeData(
      behavior: SnackBarBehavior.floating,
      backgroundColor: SsColors.navy,
      elevation: 0,
      insetPadding: const EdgeInsets.fromLTRB(SsSpace.s4, 0, SsSpace.s4, SsSpace.s4),
      contentTextStyle: const TextStyle(fontFamily: _body, fontSize: 14, fontWeight: FontWeight.w500, color: SsColors.white),
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(SsRadius.control)),
    ),
    dialogTheme: DialogThemeData(
      backgroundColor: SsColors.white,
      surfaceTintColor: Colors.transparent,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(SsRadius.sheet)),
      titleTextStyle: textTheme.headlineSmall,
      contentTextStyle: textTheme.bodyMedium,
    ),
    bottomSheetTheme: const BottomSheetThemeData(
      backgroundColor: SsColors.white,
      surfaceTintColor: Colors.transparent,
      showDragHandle: true,
      dragHandleColor: SsColors.border,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(SsRadius.sheet))),
    ),
    popupMenuTheme: PopupMenuThemeData(
      color: SsColors.white,
      surfaceTintColor: Colors.transparent,
      elevation: 6,
      shadowColor: SsColors.hairline,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(SsRadius.control)),
    ),
    badgeTheme: const BadgeThemeData(backgroundColor: SsColors.error, textStyle: TextStyle(fontFamily: _body, fontSize: 10, fontWeight: FontWeight.w700)),
    listTileTheme: ListTileThemeData(
      minVerticalPadding: SsSpace.s3,
      iconColor: SsColors.navy,
      titleTextStyle: textTheme.titleSmall,
      subtitleTextStyle: textTheme.bodySmall,
      selectedTileColor: SsColors.blue50,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(SsRadius.control)),
    ),
    switchTheme: SwitchThemeData(
      trackOutlineColor: const WidgetStatePropertyAll(Colors.transparent),
      thumbColor: const WidgetStatePropertyAll(SsColors.white),
      trackColor: WidgetStateProperty.resolveWith((s) => s.contains(WidgetState.selected) ? SsColors.blue : SsColors.border),
    ),
    checkboxTheme: CheckboxThemeData(shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(6))),
    progressIndicatorTheme: const ProgressIndicatorThemeData(color: SsColors.blue, linearTrackColor: SsColors.blue50, circularTrackColor: SsColors.blue50),
    expansionTileTheme: const ExpansionTileThemeData(iconColor: SsColors.navy, collapsedIconColor: SsColors.slate),
    pageTransitionsTheme: const PageTransitionsTheme(builders: {
      TargetPlatform.android: FadeForwardsPageTransitionsBuilder(),
      TargetPlatform.iOS: CupertinoPageTransitionsBuilder(),
    }),
  );
}
