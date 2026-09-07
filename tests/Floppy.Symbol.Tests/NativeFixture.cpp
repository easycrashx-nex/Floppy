// Never executed. Small native EXE/PDB pair for local symbol-identity and section tests.
unsigned char GUObjectArray[184];
static unsigned char NamePoolData[94144];
void* GWorld;
class UObject {
public:
    __declspec(noinline) void ProcessEvent(void*, void*);
};
void UObject::ProcessEvent(void*, void*) { GUObjectArray[0]++; }
int main() { NamePoolData[0]++; UObject object; object.ProcessEvent(0, 0); return NamePoolData[0]; }
