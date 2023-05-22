.DEFAULT_GOAL := build

all:
	build test

build:
	gcc -c signposter.c
	gcc -dynamiclib -o libsignposter.dynlib signposter.o

clean:
	rm -rf *.o *.dynlib test

test: build
	gcc -c test.c
	gcc -v test.o ./libsignposter.dynlib -o test
