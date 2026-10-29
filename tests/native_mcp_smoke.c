/* Independent benign native target for Ghidra/x64dbg integration checks. */
__declspec(dllexport) __attribute__((noinline))
int ArithmeticProbe(int value) { return value * 3 + 7; }
int main(void) { volatile int input = 5; return ArithmeticProbe(input) == 22 ? 0 : 1; }
